Imports System.Net
Imports System.IO
Imports System.Security
Imports PTSoft.MailServer

Module EmptyPOP

    Private _emails As EmailCollection
    Private _settings As MailServerSettings

    Sub Main()
        _settings = New MailServerSettings
        _settings.LoadSettings()

        _emails = New EmailCollection

        If Not String.IsNullOrWhiteSpace(_settings.MessagePath) AndAlso File.Exists(_settings.MessagePath) Then
            Using messageReader As New IO.StreamReader(_settings.MessagePath)
                Dim theMessage As New Mail.MailMessage
                theMessage.To.Add(messageReader.ReadLine)
                theMessage.From = New Mail.MailAddress(messageReader.ReadLine)
                theMessage.Subject = messageReader.ReadLine
                theMessage.Body = messageReader.ReadToEnd
                'theMessage.Headers.Add("Date", Now.ToString)
                theMessage.Headers.Add("Message-ID", _settings.MessageID)
                Dim theEmail As New EmailInfo
                theEmail.Content = FormatAsInternetMessage(theMessage)
                theEmail.Size = theEmail.Content.Length
                theEmail.ID = _settings.MessageID
                _emails.Add(theEmail)
            End Using
        End If

        Dim mailServer As New MailServer.POP3Server(_settings.MaxConnections, _settings.ResourcePoolSize)
        mailServer.AuthorizeUsername = AddressOf AuthorizeAllUsers
        mailServer.AuthorizePassword = AddressOf AuthorizeAnyPassword
        mailServer.GetMail = AddressOf GetMail

        Dim log As Log = Nothing
        If Not String.IsNullOrWhiteSpace(_settings.LogPath) Then
            log = New Log(_settings.LogPath)
        End If

        Dim cert As Cryptography.X509Certificates.X509Certificate = Nothing
        If IO.File.Exists(_settings.SecurityCertificatePath) Then
            cert = New Cryptography.X509Certificates.X509Certificate(_settings.SecurityCertificatePath)
        End If

        mailServer.Start(_settings.ListenOnIPs, _settings.ListenOnIPSecure, log, cert)

        Console.WriteLine("POP3 Server Started")
        Console.WriteLine("Press enter to stop")
        Console.ReadLine()

    End Sub

    Public Function AuthorizeAllUsers(ByVal username As String) As Boolean
        Return True
    End Function

    Public Function AuthorizeAnyPassword(ByVal username As String, ByVal password As String) As Boolean
        Return True
    End Function

    Public Function GetMail() As EmailCollection
        Return _emails
    End Function

    Public Function FormatAsInternetMessage(ByVal message As Mail.MailMessage) As Stream
        Dim sb As New Text.StringBuilder
        With sb
            For Each key In message.Headers.AllKeys
                .Append(key)
                .Append(": ")
                .Append(message.Headers(key))
                .AppendLine()
            Next
            .AppendLine("To: " & message.To.ToString)
            .AppendLine("From: " & message.From.ToString)
            .AppendLine("Subject: " & message.Subject)
            .AppendLine()
            .Append(message.Body)
        End With

        Return New MemoryStream(Text.ASCIIEncoding.ASCII.GetBytes(sb.ToString))
    End Function

End Module
