﻿Imports System.Threading
Imports PTSoft.MailServer
Imports System.Net
Imports System.IO
Imports System.Security

Public Class EmptyPOP

    Private _Worker As New Worker()
    'Used to pass a stop message from the service thread to the worker thread
    Private Shared _Stop As New ManualResetEventSlim(False)

    Protected Overrides Sub OnStart(ByVal args() As String)

        'Services cannot be debugged by the integrated debugger and the debugger must be manually attached
        'During the manual attaching process OnStart will have already run and cannot be debugged unless you uncomment the foloowing line
        'You will then be prompted to start the debugger automatically
        'Debugger.Launch()

        'The current directory will be system32 for the service so change it to the exe's directory
        Environment.CurrentDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly.Location)

        'We need to start a new thread to do the work on so OnStart can return within 30 seconds (othwise the service manager will stop the service)
        Dim WorkerThread As System.Threading.Thread
        Dim WorkerStart As System.Threading.ThreadStart
        WorkerStart = AddressOf _Worker.DoWork
        WorkerThread = New System.Threading.Thread(WorkerStart)
        WorkerThread.Start()

    End Sub

    Protected Overrides Sub OnStop()

    End Sub

    Public Class Worker
        Private _thMain As System.Threading.Thread
        Private _booMustStop As Boolean = False

        Private _emails As EmailCollection
        Private _settings As MailServerSettings

        Public Sub DoWork()
            'Startup the server
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

            'Make this thread wait until _Stop is signaled. It will then continue execution from this point
            _Stop.Wait()

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

    End Class


End Class
