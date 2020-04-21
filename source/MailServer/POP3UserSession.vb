Imports System.Net.Sockets
Imports System.Security

Public Class POP3UserSession


    Public Enum SessionState
        Authorization
        Transaction
    End Enum

    Dim _stream As IO.Stream
    Dim _isSecure As Boolean

    Public Property Socket As Socket
    Public Property State As SessionState?
    Public Property Username As String
    Public Property Password As String
    Public Property MessagesRetrieved As Integer

    Public Property IsSecure As Boolean
        Get
            Return _isSecure
        End Get
        Set(ByVal value As Boolean)
            If value <> _isSecure Then
                _isSecure = value
                _stream = Nothing
            End If
        End Set
    End Property

    Public ReadOnly Property Stream As IO.Stream
        Get
            If _stream Is Nothing AndAlso _Socket IsNot Nothing Then
                If IsSecure Then
                    _stream = New Net.Security.SslStream(New NetworkStream(_Socket, False), True, AddressOf RemoteCertificateValidationCallback)
                Else
                    _stream = New NetworkStream(_Socket)
                End If
            End If

            Return _stream
        End Get
    End Property


    ''' <summary>
    ''' Reset the token so it can be reused
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub Clear()
        If _Stream IsNot Nothing Then
            Try
                _Stream.Dispose()
            Catch ex As Exception
                'Dont worry about it
            End Try
        End If

        _Stream = Nothing
        _Socket = Nothing
        _State = Nothing
        _Username = Nothing
        _Password = Nothing
        _isSecure = False
        _MessagesRetrieved = 0
    End Sub

    Private Function RemoteCertificateValidationCallback(ByVal sender As Object, ByVal certificate As Cryptography.X509Certificates.X509Certificate, ByVal chain As Cryptography.X509Certificates.X509Chain, ByVal sslPolicyErrors As Net.Security.SslPolicyErrors) As Boolean
        Return True
    End Function

End Class
