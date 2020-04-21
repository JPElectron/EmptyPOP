Imports System.Net.Sockets
Imports System.Threading
Imports System.Net
Imports System.Security
Imports System.Text
Imports System.Net.Security

''' <summary>
''' Handles the details of client connections and session.
''' It is up to the implementation to handle the authentication and retrieval of mail messages
''' </summary>
''' <remarks></remarks>
Public Class POP3Server
    ''' <summary>
    ''' The valid statuses for responses to POP3 queries
    ''' </summary>
    ''' <remarks></remarks>
    Public Class StatusIndicator
        ''' <summary>
        ''' +OK
        ''' </summary>
        ''' <remarks></remarks>
        Public Shared ReadOnly OK As Byte() = New Byte() {43, 79, 75, 32}
        ''' <summary>
        ''' -ERR
        ''' </summary>
        ''' <remarks></remarks>
        Public Shared ReadOnly [Error] As Byte() = New Byte() {45, 69, 82, 82, 32} '-ERR in ASCII
    End Class

    Public Const DefaultPort As Integer = 110
    Public Const DefaultSecurePort As Integer = 995
    'POP3 specifies a Max of 512 bytes for queries and single line responses
    Private Const _defaultRecieveBufferSize As Integer = 512
    Private Const _defaultPoolCount As Integer = 250
    Private Const _opsToPreAlloc As Integer = 1   ' read and write can share

    Private _reciveBufferSize As Integer
    Private _readWritePool As Collections.ObjectPool(Of SocketAsyncEventArgs)
    Private _bufferManager As BufferManager
    Private _maxNumberOfClients As Integer?
    Private _maxConnectedClientsSemp As SemaphoreSlim

    Private _listenSockets As List(Of Socket)
    Private _secureListenSockets As List(Of Socket)

    Private _log As Log

    Private _welcomeMessage() As Byte
    Private _endLine As Byte() = New Byte() {13, 10} 'CRLF in ASCII bytes
    Private _endMultiLineResponse = New Byte() {13, 10, 46, 13, 10}
    Private _capas As String = ControlChars.NewLine & "STLS" & ControlChars.NewLine & "UIDL" & ControlChars.NewLine & "."

    'Callback methods for the server implementation to handle authentication and mail processing 
    Public Property AuthorizeUsername As Func(Of String, Boolean)
    Public Property AuthorizePassword As Func(Of String, String, Boolean)
    Public Property GetMail As Func(Of EmailCollection)

    Private _secureConnectionCert As Cryptography.X509Certificates.X509Certificate

    ''' <summary>
    ''' The POP3 Server
    ''' </summary>
    ''' <param name="maxNumberOfClients">The maximum number of clients that can be connected concurrently.
    ''' If the number of connected clients exceeds this they will wait until existing clients disconnect.
    ''' Pass Null to allow unlimited connections.</param>
    ''' <param name="resourcePoolCount">The number of objets to preallocate to handle client sesstions.
    ''' If the number of concurrently connected clients exceeds this new objects will be allocated to handle the session.</param>
    ''' <param name="welcomeMessage">The messsage to send to the client when they initially connect.</param>
    ''' <remarks></remarks>
    Public Sub New(Optional ByVal maxNumberOfClients As Integer? = Nothing, Optional ByVal resourcePoolCount As Integer? = Nothing, Optional ByVal welcomeMessage As String = "POP3 server ready")

        _maxNumberOfClients = maxNumberOfClients
        _reciveBufferSize = _defaultRecieveBufferSize

        If Not resourcePoolCount.HasValue Then
            resourcePoolCount = _defaultPoolCount
            If _maxNumberOfClients < resourcePoolCount Then
                'No point in making the pool larger than the max number of clients allowed
                resourcePoolCount = _maxNumberOfClients
            End If
        End If

        'A large continious beffer to avoid heap fragmentation
        _bufferManager = New BufferManager(_reciveBufferSize * _opsToPreAlloc * resourcePoolCount, _reciveBufferSize)
        _bufferManager.InitBuffer()

        'Thread safe pool of objects to handle each clients session info
        _readWritePool = New Collections.ObjectPool(Of SocketAsyncEventArgs)(AddressOf CreateSocketAsyncEventArgs, resourcePoolCount)


        If _maxNumberOfClients.HasValue Then
            'Controls the max numbe of clients allowed to connect
            _maxConnectedClientsSemp = New SemaphoreSlim(_maxNumberOfClients, _maxNumberOfClients)
        End If

        _welcomeMessage = Text.ASCIIEncoding.ASCII.GetBytes(welcomeMessage & " " & Environment.CurrentDirectory)
    End Sub

    ''' <summary>
    ''' Initializes a new object to handle the client session
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function CreateSocketAsyncEventArgs() As SocketAsyncEventArgs
        Dim readWriteEventArg As New SocketAsyncEventArgs()
        AddHandler readWriteEventArg.Completed, New EventHandler(Of SocketAsyncEventArgs)(AddressOf IO_Completed)
        readWriteEventArg.UserToken = New POP3UserSession()

        'assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
        If Not _bufferManager.SetBuffer(readWriteEventArg) Then
            'We are creating a socketeventargs without free bufffer space
            'So we need to create a buffer that is specific to this object which is not ideal as these individual buffers will get pinned in the heap
            readWriteEventArg.SetBuffer(New Byte(_reciveBufferSize * _opsToPreAlloc) {}, 0, _reciveBufferSize * _opsToPreAlloc)
        End If

        Return readWriteEventArg
    End Function

    ''' <summary>
    ''' Starts the POP3 server listening for connections
    ''' </summary>
    ''' <param name="endPoints">Collection of end points to listen</param>
    ''' <param name="sslEndPoints">Collection of end points to listen for TLS/SSL connections</param>
    ''' <param name="log">Optional log for activity</param>
    ''' <param name="securityCert">The security certificate to use for TLS/SSL connections</param>
    ''' <remarks>The server will only listen for secure connectoins if a valid security sertificate is specififed</remarks>
    Public Sub Start(ByVal endPoints As IEnumerable(Of IPEndPoint), ByVal sslEndPoints As IEnumerable(Of IPEndPoint), Optional ByVal log As Log = Nothing, Optional ByVal securityCert As Cryptography.X509Certificates.X509Certificate = Nothing)
        _log = log
        _secureConnectionCert = securityCert

        _listenSockets = New List(Of Socket)(endPoints.Count)

        For Each endPoint As IPEndPoint In endPoints
            Try
                Dim listenSocket As New Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                listenSocket.Bind(endPoint)
                listenSocket.Listen(100)
                _listenSockets.Add(listenSocket)
                StartAccept(Nothing, listenSocket)
            Catch ex As Exception
                'We will get an exception if we cant bind to the socket, just ignore
            End Try
        Next

        If _secureConnectionCert IsNot Nothing AndAlso sslEndPoints IsNot Nothing Then
            _secureListenSockets = New List(Of Socket)(sslEndPoints.Count)

            For Each endPoint As IPEndPoint In sslEndPoints
                Try
                    Dim secureListenSocket As New Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    secureListenSocket.Bind(endPoint)
                    secureListenSocket.Listen(100)
                    _secureListenSockets.Add(secureListenSocket)
                    StartAccept(Nothing, secureListenSocket)
                Catch ex As Exception
                    'We will get an exception if we cant bind to the socket, just ignore
                End Try

            Next
#If DEBUG Then
        Else
            If sslEndPoints IsNot Nothing AndAlso sslEndPoints.Count > 0 Then
                Debug.WriteLine("Unable to listen on secure sockets. Certificate is missing")
            End If
#End If
        End If
    End Sub

    ''' <summary>
    ''' Begins an operation to accept a connection request from the client 
    ''' </summary>
    ''' <param name="acceptEventArg">The context object to use when issuing the accept operation on the server's listening socket</param>
    ''' <remarks></remarks>
    Public Sub StartAccept(ByVal acceptEventArg As SocketAsyncEventArgs, Optional ByVal listenSocket As Socket = Nothing)

        If acceptEventArg Is Nothing Then
            acceptEventArg = New SocketAsyncEventArgs()
            AddHandler acceptEventArg.Completed, New EventHandler(Of SocketAsyncEventArgs)(AddressOf AcceptEventArg_Completed)
        Else
            ' socket must be cleared since the context object is being reused
            acceptEventArg.AcceptSocket = Nothing
        End If


        If _maxConnectedClientsSemp IsNot Nothing Then
            'If we have a max number of connection to work with, check and wait here if they are all in use
            _maxConnectedClientsSemp.Wait()
        End If

#If DEBUG Then
        Debug.WriteLine("Listening on: " & listenSocket.LocalEndPoint.ToString)
#End If

        If Not listenSocket.AcceptAsync(acceptEventArg) Then
            ProcessAccept(acceptEventArg)
        End If
    End Sub


    ''' <summary>
    ''' This method is the callback method associated with Socket.AcceptAsync 
    ''' operations and is invoked when an accept operation is complete
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e"></param>
    ''' <remarks></remarks>
    Private Sub AcceptEventArg_Completed(ByVal sender As Object, ByVal e As SocketAsyncEventArgs)
        ProcessAccept(e)
    End Sub

    ''' <summary>
    ''' Accepts a client connection
    ''' </summary>
    ''' <param name="e"></param>
    ''' <remarks></remarks>
    Private Sub ProcessAccept(ByVal e As SocketAsyncEventArgs)

        ' Get the socket for the accepted client connection and put it into the ReadEventArg object user token
        Dim readEventArgs As SocketAsyncEventArgs = _readWritePool.GetObject
        Dim token As POP3UserSession = DirectCast(readEventArgs.UserToken, POP3UserSession)
        token.Socket = e.AcceptSocket

        'Accept the next connection request on the socket that this accept came in on
        Dim listenEndpoint As IPEndPoint = e.AcceptSocket.LocalEndPoint
        Dim listenSocket As Socket = Nothing
        If listenEndpoint IsNot Nothing Then
            'Determine if the connection needs to be secured
            For Each socket As Socket In _listenSockets
                If listenEndpoint.Equals(socket.LocalEndPoint) Then
                    listenSocket = socket
                    token.IsSecure = False
                    Exit For
                End If
            Next

            If _secureListenSockets IsNot Nothing Then
                For Each socket As Socket In _secureListenSockets
                    If listenEndpoint.Equals(socket.LocalEndPoint) Then
                        listenSocket = socket
                        token.IsSecure = True
                        Exit For
                    End If
                Next
            End If
        End If

#If DEBUG Then
        Debug.WriteLine("Accepted Connection (" & token.Socket.RemoteEndPoint.ToString & ") " & If(token.IsSecure, " - Secure", " - Not Secure"))
#End If

        If token.IsSecure Then
            'Authenticate the connection
            DirectCast(token.Stream, Net.Security.SslStream).BeginAuthenticateAsServer(_secureConnectionCert, AddressOf EndAthenticateAsServer, readEventArgs)
        Else
            'Set them to the next state after the connection is accepted
            token.State = POP3UserSession.SessionState.Authorization

            'Now the the client connection has been accepted send the welcome message
            'Welcome message is always OK
            SendResponseToClient(readEventArgs, StatusIndicator.OK, _welcomeMessage)
            'After sending the message the server will be setup to recieve a response from the client
        End If

        'listen on the socket this came in on again
        If listenSocket IsNot Nothing Then StartAccept(e, listenSocket)
    End Sub

    Private Sub EndAthenticateAsServer(ByVal result As IAsyncResult)
        'Now that the client is authenticated send the welcome message
        Dim e As SocketAsyncEventArgs = DirectCast(result.AsyncState, SocketAsyncEventArgs)
        Dim token As POP3UserSession = DirectCast(e.UserToken, POP3UserSession)
        Try
            DirectCast(token.Stream, SslStream).EndAuthenticateAsServer(result)
        Catch ex As Exception
            'Authentication failed so clean up the connection and do not continue
            CloseClientSocket(e)
            Exit Sub
        End Try

        token.State = POP3UserSession.SessionState.Authorization
        SendResponseToClient(e, StatusIndicator.OK, _welcomeMessage)
    End Sub

    ''' <summary>
    ''' This method is called whenever a receive or send operation is completed on a socket 
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
    ''' <remarks></remarks>
    Private Sub IO_Completed(ByVal sender As Object, ByVal e As SocketAsyncEventArgs)
        ' determine which type of operation just completed and call the associated handler
        Select Case e.LastOperation
            Case SocketAsyncOperation.Receive, SocketAsyncOperation.ReceiveFrom
                ProcessReceive(e)
                Exit Select
            Case SocketAsyncOperation.Send, SocketAsyncOperation.SendTo
                ProcessSend(e)
                Exit Select
#If DEBUG Then
            Case Else
                Throw New ArgumentException("The last operation completed on the socket was not a receive or send")
#End If
        End Select

    End Sub

    ''' <summary>
    ''' Callback for ansyncronous read operation
    ''' </summary>
    ''' <param name="result"></param>
    ''' <remarks></remarks>
    Private Sub DataRecieved(ByVal result As IAsyncResult)
        Dim e As SocketAsyncEventArgs = DirectCast(result.AsyncState, SocketAsyncEventArgs)
        Dim token As POP3UserSession = DirectCast(e.UserToken, POP3UserSession)
        Try
            If token.Stream IsNot Nothing Then
                Dim bytesRead As Integer = token.Stream.EndRead(result)
                ProcessReceive(e, bytesRead)
            End If
        Catch ex As Exception
            'Will get an exception if the client closed the socket
            CloseClientSocket(e)
        End Try
    End Sub

    ''' <summary>
    ''' This method is invoked when an asynchronous receive operation completes. 
    ''' If the remote host closed the connection, then the socket is closed.  
    ''' If data was received then the data is echoed back to the client.
    ''' </summary>
    ''' <param name="e"></param>
    ''' <remarks></remarks>
    Private Sub ProcessReceive(ByVal e As SocketAsyncEventArgs, Optional ByVal bytesRead As Integer? = Nothing)
        If bytesRead Is Nothing Then bytesRead = e.BytesTransferred
        ' check if the remote host closed the connection
        Dim token As POP3UserSession = DirectCast(e.UserToken, POP3UserSession)
        If bytesRead = 0 Then
            'Non-secured client is closing the socket
            CloseClientSocket(e)
            Exit Sub
        End If

        If token.State.HasValue Then
            'Get the command sent
            Dim recieved As String = Text.ASCIIEncoding.ASCII.GetString(e.Buffer, e.Offset, bytesRead)
            If recieved.EndsWith(ControlChars.NewLine) Then recieved = recieved.Substring(0, recieved.Length - 2)

#If DEBUG Then
            Debug.WriteLine("Recieved " & bytesRead.ToString & " bytes : (" & token.Socket.RemoteEndPoint.ToString & ") " & recieved)
#End If
            Dim seperatorIndex As Integer = recieved.IndexOf(" ")
            Dim command As String = String.Empty
            Dim params As String = String.Empty

            If seperatorIndex > 0 Then
                command = recieved.Substring(0, recieved.IndexOf(" "))
                params = recieved.Substring(seperatorIndex + 1, recieved.Length - seperatorIndex - 1)
            Else
                command = recieved
            End If

            If command.Equals("QUIT", StringComparison.OrdinalIgnoreCase) Then
                SendResponseToClient(e, StatusIndicator.OK, "Goodbye")
                CloseClientSocket(e)
                Exit Sub
            End If

            'Make sure the command issued is valid for the state the session is in
            Select Case token.State
                Case POP3UserSession.SessionState.Authorization 'State after successful connect
                    Select Case command.ToUpperInvariant
                        Case "CAPA" 'Server capabilities
                            SendResponseToClient(e, StatusIndicator.OK, _capas)
                        Case "USER"
                            If Not String.IsNullOrWhiteSpace(params) Then
                                If _AuthorizeUsername IsNot Nothing AndAlso _AuthorizeUsername(params) Then
                                    token.Username = params
                                    SendResponseToClient(e, StatusIndicator.OK, "OK send PASS")
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "User is not authorized")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Username is required")
                            End If
                        Case "PASS"
                            If Not String.IsNullOrWhiteSpace(token.Username) Then
                                If Not String.IsNullOrWhiteSpace(params) Then
                                    If _AuthorizePassword IsNot Nothing AndAlso _AuthorizePassword(token.Username, params) Then
                                        token.Password = params
                                        SendResponseToClient(e, StatusIndicator.OK, "User is authenticated")
                                        token.State = POP3UserSession.SessionState.Transaction
                                    Else
                                        SendResponseToClient(e, StatusIndicator.Error, "Password is incorrect")
                                    End If
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "Password is required")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Username is required")
                            End If
                        Case "STLS"
                            If Not token.IsSecure Then
                                'Send OK
                                token.Stream.Write(StatusIndicator.OK, 0, StatusIndicator.OK.Length)
                                token.Stream.Write(_endLine, 0, 2)
                                token.IsSecure = True
                                DirectCast(token.Stream, Net.Security.SslStream).BeginAuthenticateAsServer(_secureConnectionCert, AddressOf EndAthenticateAsServer, e)
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "STLS is not allowed when the connection is already secured")
                            End If
                        Case Else
                            SendResponseToClient(e, StatusIndicator.Error, "Command not supported")
                    End Select

                Case POP3UserSession.SessionState.Transaction 'State after successful authorization
                    Select Case command.ToUpperInvariant
                        Case "STAT"
                            If _GetMail IsNot Nothing Then
                                Dim totalSize As Long
                                Dim emails As EmailCollection = _GetMail()
                                If emails IsNot Nothing Then
                                    For Each email As EmailInfo In emails
                                        totalSize += email.Size
                                    Next
                                    SendResponseToClient(e, StatusIndicator.OK, emails.Count.ToString & " " & totalSize.ToString)
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                            End If
                        Case "LIST"
                            If _GetMail IsNot Nothing Then
                                Dim emails As EmailCollection = _GetMail()
                                If emails IsNot Nothing Then
                                    If String.IsNullOrWhiteSpace(params) Then
                                        'List all the emailmessages
                                        If emails IsNot Nothing Then
                                            Dim emailListing As New StringBuilder(ControlChars.NewLine)
                                            For i As Integer = 0 To emails.Count - 1
                                                Dim email As EmailInfo = emails(i)
                                                With emailListing
                                                    .Append((i + 1).ToString)
                                                    .Append(" ")
                                                    .Append(email.Size.ToString)
                                                    .Append(ControlChars.NewLine)
                                                End With
                                            Next
                                            'Multi-line response termination character
                                            emailListing.Append(".")
                                            SendResponseToClient(e, StatusIndicator.OK, emailListing.ToString)
                                        End If
                                    Else
                                        Dim messageNumber As Integer
                                        If Integer.TryParse(params, messageNumber) Then
                                            If Not (messageNumber < 1 OrElse messageNumber > emails.Count) Then
                                                SendResponseToClient(e, StatusIndicator.OK, messageNumber.ToString & " " & emails(messageNumber - 1).Size)
                                            Else
                                                SendResponseToClient(e, StatusIndicator.Error, "Message does not exist")
                                            End If
                                        Else
                                            SendResponseToClient(e, StatusIndicator.Error, "Parameter is not a number")
                                        End If
                                    End If
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                            End If
                        Case "UIDL"
                            If _GetMail IsNot Nothing Then
                                Dim emails As EmailCollection = _GetMail()
                                If emails IsNot Nothing Then
                                    If String.IsNullOrWhiteSpace(params) Then
                                        'List all the emailmessages
                                        If emails IsNot Nothing Then
                                            Dim emailListing As New StringBuilder(ControlChars.NewLine)
                                            For i As Integer = 0 To emails.Count - 1
                                                Dim email As EmailInfo = emails(i)
                                                With emailListing
                                                    .Append((i + 1).ToString)
                                                    .Append(" ")
                                                    .Append(email.ID)
                                                    .Append(ControlChars.NewLine)
                                                End With
                                            Next
                                            'Multi-line response termination character
                                            emailListing.Append(".")
                                            SendResponseToClient(e, StatusIndicator.OK, emailListing.ToString)
                                        End If
                                    Else
                                        Dim messageNumber As Integer
                                        If Integer.TryParse(params, messageNumber) Then
                                            If Not (messageNumber < 1 OrElse messageNumber > emails.Count) Then
                                                SendResponseToClient(e, StatusIndicator.OK, messageNumber.ToString & " " & emails(messageNumber - 1).ID)
                                            Else
                                                SendResponseToClient(e, StatusIndicator.Error, "Message does not exist")
                                            End If
                                        Else
                                            SendResponseToClient(e, StatusIndicator.Error, "Parameter is not a number")
                                        End If
                                    End If
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                            End If
                        Case "RETR"
                            If _GetMail IsNot Nothing Then
                                Dim emails As EmailCollection = _GetMail()
                                If emails IsNot Nothing Then
                                    If Not String.IsNullOrWhiteSpace(params) Then
                                        Dim messageNumber As Integer
                                        If Integer.TryParse(params, messageNumber) Then
                                            If Not (messageNumber < 1 OrElse messageNumber > emails.Count) Then
                                                SendResponseToClient(e, StatusIndicator.OK, emails(messageNumber - 1).Content)

                                                token.MessagesRetrieved += 1
                                            Else
                                                SendResponseToClient(e, StatusIndicator.Error, "Message does not exist")
                                            End If
                                        Else
                                            SendResponseToClient(e, StatusIndicator.Error, "Parameter is not a number")
                                        End If
                                    Else
                                        SendResponseToClient(e, StatusIndicator.Error, "Message number is required")
                                    End If
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                            End If
                        Case "DELE"
                            If _GetMail IsNot Nothing Then
                                Dim emails As EmailCollection = _GetMail()
                                If emails IsNot Nothing Then
                                    If Not String.IsNullOrWhiteSpace(params) Then
                                        Dim messageNumber As Integer
                                        If Integer.TryParse(params, messageNumber) Then
                                            If Not (messageNumber < 1 OrElse messageNumber > emails.Count) Then
                                                emails(messageNumber - 1).IsDeleted = True
                                                SendResponseToClient(e, StatusIndicator.OK, String.Empty)
                                            Else
                                                SendResponseToClient(e, StatusIndicator.Error, "Message does not exist")
                                            End If
                                        Else
                                            SendResponseToClient(e, StatusIndicator.Error, "Parameter is not a number")
                                        End If
                                    Else
                                        SendResponseToClient(e, StatusIndicator.Error, "Message number is required")
                                    End If
                                Else
                                    SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                                End If
                            Else
                                SendResponseToClient(e, StatusIndicator.Error, "Cannot get info for mail box")
                            End If
                        Case "NOOP" 'Does Nothing but reset the timeout if there is one
                            SendResponseToClient(e, StatusIndicator.OK, String.Empty)
                        Case Else
                            SendResponseToClient(e, StatusIndicator.Error, "Command not supported")
                    End Select


                Case Else
                    'We are in some unknown state somehow
                    CloseClientSocket(e)
            End Select

        Else
            'We have no state so something got messed up somewhere
            SendResponseToClient(e, StatusIndicator.Error, "User has no state information")
            CloseClientSocket(e)
        End If
    End Sub


    ''' <summary>
    '''  This method is invoked when an asynchronous send operation completes.  
    '''  The method issues another receive on the socket to read any additional 
    '''  data sent from the client
    ''' </summary>
    ''' <param name="e"></param>
    ''' <remarks></remarks>
    Private Sub ProcessSend(ByVal e As SocketAsyncEventArgs)
        If e.SocketError = SocketError.Success Then
            ' done echoing data back to the client
            Dim token As POP3UserSession = DirectCast(e.UserToken, POP3UserSession)
            ' read the next block of data send from the client
            If token.Socket IsNot Nothing AndAlso Not token.Socket.ReceiveAsync(e) Then
                ProcessReceive(e)
            End If
        Else
            CloseClientSocket(e)
        End If
    End Sub

    ''' <summary>
    ''' Closes the connection, ends and resets the session to be resued
    ''' </summary>
    ''' <param name="e"></param>
    ''' <remarks></remarks>
    Private Sub CloseClientSocket(ByVal e As SocketAsyncEventArgs)
        Dim token As POP3UserSession = TryCast(e.UserToken, POP3UserSession)

        If token.Socket Is Nothing Then
            'The client closed to connection as the server did so this is the second attempt to close the same connection
            Exit Sub
        End If

        ' close the socket associated with the client
        Try
            token.Socket.Shutdown(SocketShutdown.Send)
            ' throws if client process has already closed
            token.Socket.Close()
        Catch ex As Exception
            'so don't worry
        End Try


        If _log IsNot Nothing AndAlso Not String.IsNullOrEmpty(token.Username) Then
            _log.BeginWriteEntry(String.Format("{0} {1} {2}", token.Username, token.Password, token.MessagesRetrieved))
        End If

        ' Free the SocketAsyncEventArg so they can be reused by another client
        token.Clear()
        _readWritePool.PutObject(e)

        If _maxConnectedClientsSemp IsNot Nothing Then
            'Let any other connections continue if waiting
            _maxConnectedClientsSemp.Release()
        End If

#If DEBUG Then
        Debug.WriteLine("Connection Closed")
#End If

    End Sub

    ''' <summary>
    ''' Sends a response string to the client.
    ''' </summary>
    ''' <param name="e"></param>
    ''' <param name="statusIndicator"></param>
    ''' <param name="message"></param>
    ''' <remarks>Endline is added to the response</remarks>
    Public Sub SendResponseToClient(ByVal e As SocketAsyncEventArgs, ByRef statusIndicator As Byte(), ByRef message As String)
        SendResponseToClient(e, statusIndicator, If(String.IsNullOrEmpty(message), Nothing, Text.ASCIIEncoding.ASCII.GetBytes(message)))
    End Sub

    ''' <summary>
    ''' Sends a response string to the client.
    ''' </summary>
    ''' <param name="e"></param>
    ''' <param name="statusIndicator"></param>
    ''' <param name="messageBytes"></param>
    ''' <remarks>Endline is added to the response</remarks>
    Public Sub SendResponseToClient(ByVal e As SocketAsyncEventArgs, ByRef statusIndicator As Byte(), ByRef messageBytes As Byte())
        Dim token As POP3UserSession = DirectCast(e.UserToken, POP3UserSession)
        Dim messageLength As Integer = If(messageBytes IsNot Nothing, messageBytes.Length, 0)

        Try
            token.Stream.Write(statusIndicator, 0, statusIndicator.Count)
            If messageLength > 0 Then token.Stream.Write(messageBytes, 0, messageBytes.Length)
            token.Stream.Write(_endLine, 0, 2)

#If DEBUG Then
            Debug.WriteLine("Sent " & If(token.IsSecure, "secure ", String.Empty) & " (" & token.Socket.RemoteEndPoint.ToString & ") " & Text.ASCIIEncoding.ASCII.GetString(statusIndicator) & If(messageLength > 0, Text.ASCIIEncoding.ASCII.GetString(messageBytes), String.Empty))
#End If
            If token.IsSecure Then
                'We need to read from the stream to getthe decryped response
                token.Stream.BeginRead(e.Buffer, e.Offset, _reciveBufferSize, AddressOf DataRecieved, e)
            Else
                'No secure so we can read the raw bytes, this is more efficent
                If Not token.Socket.ReceiveAsync(e) Then
                    ProcessReceive(e)
                End If
            End If

        Catch ex As Exception
            'The client could close the connection while we are trying to write, and that will throw an exception
            CloseClientSocket(e)
        End Try
    End Sub

    ''' <summary>
    ''' Sends a response string to the client.
    ''' </summary>
    ''' <param name="e"></param>
    ''' <param name="statusIndicator"></param>
    ''' <param name="content"></param>
    ''' <remarks>Endline is added to the response</remarks>
    Public Sub SendResponseToClient(ByVal e As SocketAsyncEventArgs, ByRef statusIndicator As Byte(), ByVal content As IO.Stream)
        Dim token As POP3UserSession = DirectCast(e.UserToken, POP3UserSession)
        content.Seek(0, IO.SeekOrigin.Begin)

        Try
            token.Stream.Write(statusIndicator, 0, statusIndicator.Length)
            token.Stream.Write(_endLine, 0, 2)
            content.CopyTo(token.Stream, _reciveBufferSize)
            token.Stream.Write(_endMultiLineResponse, 0, 5)

            token.Stream.BeginRead(e.Buffer, e.Offset, _reciveBufferSize, AddressOf DataRecieved, e)
        Catch ex As Exception
            'The client could close the connection while we are trying to write, and that will throw an exception
            CloseClientSocket(e)
        End Try

#If DEBUG Then
        Debug.WriteLine("Sent Stream")
#End If
    End Sub

End Class
