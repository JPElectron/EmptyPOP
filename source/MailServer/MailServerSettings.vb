Imports System.IO
Imports System.Net
Imports System.Net.NetworkInformation

''' <summary>
''' Parses an .ini file with settings for the server
''' </summary>
''' <remarks></remarks>
Public Class MailServerSettings
    Public Const DefaultSettingsPath As String = "settings.ini"

    Public Property ListenOnIPs As List(Of IPEndPoint)
    Public Property ListenOnIPSecure As List(Of IPEndPoint)
    Public Property SecurityCertificatePath As String
    Public Property MessagePath As String
    Public Property MessageID As String
    Public Property LogPath As String
    Public Property MaxConnections As Integer?
    Public Property ResourcePoolSize As Integer?
    Private Const CommentDelimiter = ";"

    ''' <summary>
    ''' Does the actual parsing
    ''' </summary>
    ''' <param name="path">The path to the .ini file.</param>
    ''' <remarks></remarks>
    Public Sub LoadSettings(Optional ByVal path As String = Nothing)
        If String.IsNullOrWhiteSpace(path) Then path = DefaultSettingsPath
        Dim ips As New List(Of IPAddress)
        Dim secureIps As New List(Of IPAddress)
        Dim ports As New List(Of Integer)
        Dim securePorts As New List(Of Integer)

        If File.Exists(path) Then
            Using reader As New StreamReader(path)
                While Not reader.EndOfStream
                    Dim SettingsLine As String = reader.ReadLine()
                    If String.IsNullOrEmpty(SettingsLine) OrElse SettingsLine.StartsWith(CommentDelimiter) Then Continue While
                    Dim setting() As String = SettingsLine.Split("=".ToCharArray, StringSplitOptions.RemoveEmptyEntries)

                    If setting.Length = 2 Then
                        Select Case setting(0).ToUpperInvariant
                            Case "LISTENONIP"
                                For Each ip As String In setting(1).Split(",".ToCharArray, StringSplitOptions.RemoveEmptyEntries)
                                    Dim ipToAdd As IPAddress = Nothing
                                    If IPAddress.TryParse(ip.Replace(" ", String.Empty), ipToAdd) Then
                                        ips.Add(ipToAdd)
                                    End If
                                Next
                            Case "PORT"
                                For Each port As String In setting(1).Split(",".ToCharArray, StringSplitOptions.RemoveEmptyEntries)
                                    Dim portToAdd As Integer
                                    If Integer.TryParse(port.Replace(" ", String.Empty), portToAdd) Then
                                        ports.Add(portToAdd)
                                    End If
                                Next
                            Case "SECUREPORT"
                                For Each port As String In setting(1).Split(",".ToCharArray, StringSplitOptions.RemoveEmptyEntries)
                                    Dim portToAdd As Integer
                                    If Integer.TryParse(port.Replace(" ", String.Empty), portToAdd) Then
                                        securePorts.Add(portToAdd)
                                    End If
                                Next
                            Case "MESSAGE"
                                _MessagePath = setting(1)
                            Case "MESSAGEID"
                                _MessageID = setting(1)
                            Case "SECURITYCERTIFICATE"
                                _SecurityCertificatePath = setting(1)
                            Case "LOG"
                                _LogPath = setting(1)
                            Case "MAXCONNECTIONS"
                                Dim maxCons As Integer
                                If Integer.TryParse(setting(1), maxCons) Then
                                    _MaxConnections = maxCons
                                End If
                            Case "RESOURCEPOOL"
                                Dim res As Integer
                                If Integer.TryParse(setting(1), res) Then
                                    _ResourcePoolSize = res
                                End If
                        End Select
                    End If
                End While
            End Using
        End If

        If ports.Count = 0 Then
            ports.Add(POP3Server.DefaultPort)
        End If

        If String.IsNullOrWhiteSpace(_SecurityCertificatePath) Then
            _SecurityCertificatePath = "security.cer"
        End If

        If securePorts.Count = 0 AndAlso File.Exists(_SecurityCertificatePath) Then
            securePorts.Add(POP3Server.DefaultSecurePort)
        End If

        _ListenOnIPs = New List(Of IPEndPoint)

        If ips.Count > 0 Then
            For Each ip As IPAddress In ips
                For Each port As Integer In Ports
                    _ListenOnIPs.Add(New IPEndPoint(ip, port))
                Next
            Next
        Else
            'Bind to all IPs
            For Each nic As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus = OperationalStatus.Up Then
                    Dim ipProps As IPInterfaceProperties = nic.GetIPProperties()
                    Try
                        For Each unicastIP As UnicastIPAddressInformation In ipProps.UnicastAddresses
                            If unicastIP.DuplicateAddressDetectionState = DuplicateAddressDetectionState.Preferred Then
                                'IPv4 for now
                                If unicastIP.Address.AddressFamily = Sockets.AddressFamily.InterNetwork Then
                                    For Each port As Integer In Ports
                                        _ListenOnIPs.Add(New IPEndPoint(unicastIP.Address, port))
                                    Next
                                End If
                            End If
                        Next
                    Catch ex As Exception
                        'Above is only supported on XP or higher so use the crappy way
                        For Each hostIP As IPAddress In System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName).AddressList
                            For Each port As Integer In Ports
                                _ListenOnIPs.Add(New IPEndPoint(hostIP, port))
                            Next
                        Next
                    End Try
                End If
            Next
        End If

        _ListenOnIPSecure = New List(Of IPEndPoint)
        If securePorts.Count > 0 Then
            If secureIps.Count > 0 Then
                For Each ip As IPAddress In ips
                    For Each port As Integer In securePorts
                        _ListenOnIPSecure.Add(New IPEndPoint(ip, port))
                    Next
                Next
            Else
                'Use the unsecure IPs to add secure end points
                For Each ep As IPEndPoint In _ListenOnIPs
                    For Each port As Integer In securePorts
                        Dim secureEP = New IPEndPoint(ep.Address, port)
                        If Not _ListenOnIPSecure.Contains(secureEP) Then _ListenOnIPSecure.Add(secureEP)
                    Next
                Next
            End If
        End If

        If String.IsNullOrWhiteSpace(_MessagePath) Then
            _MessagePath = "message.txt"
        End If

        If String.IsNullOrWhiteSpace(_MessageID) AndAlso IO.File.Exists(_MessagePath) Then
            'If not explicitly specified, use the last modifed date of the message file for the ID
            Dim messageFileInfo As New FileInfo(_MessagePath)
            _MessageID = messageFileInfo.LastWriteTime.Ticks.ToString
        End If


    End Sub




End Class
