# EmptyPOP
Delivers only 1 email (or none) from any POP request, regardless of user credentials.

Perhaps you have a [legacy] application...

- that must send e-mail through software (like Outlook or Outlook Express)
- that doesn't work right when it uses a mailto: [to Gmail] link

And...

- you don't want new mail arriving at a local inbox
- you want to encourage/force users to reach their inbox using the web interface

Or...

- you're frustrated by users who configure POP3 for themselves, but then regret it,
   and you end up having to import all their mail back to Gmail or the web interface
Then EmptyPOP is just what you're looking for!

You can...

- Configure the 1 message (or no message) that gets delivered to the inbox
- Run the service on other ports besides 110
- Use your own SSL cert
- Log all usernames/passwords to a file...
   useful to discover users who decided to configure POP for themselves
   useful to recover an unknown POP account password saved in an email client

EmptyPOP can also be used when your mail server is down or unavailable...
Simply direct POP traffic to the IP of the EmptyPOP server, which will deliver 1 message to all clients regardless of their login.
You could use this message to explain that you know of the issue and when you expect normal service to resume.

Tested on Windows XP, Server 2003, Vista, Server 2008, Windows 7

This program runs as a service; without any GUI, taskbar, or system tray icon.

<b>Installation:</b>

1) Ensure the Microsoft .NET Framework 4.x is installed
2) Run emptypop-setup.msi and follow the wizard
3) Modify C:\EMPTYPOP\settings.ini as indicated (see comments within the file)

<b>Usage:</b>

- How can I make my own SSL Certificate so SECUREPORT= will work?

      Download and install the Windows SDK
      It's not necessary to install the full .NET Framework if prompted.
      Select only Tools under Windows Native Code Development, see screenshot.
      Go to Start > Programs > Microsoft Windows SDK v7.1 > Windows SDK 7.1 Command Prompt
      Ensure you are in the Bin folder, the path should look like: C:\Program Files\Microsoft SDKs\Windows\v7.1\Bin>
      If not, type: cd Bin
      Enter
      type: makecert -pe -n "CN=Test And Dev Root Authority" -ss my -sr LocalMachine -a sha1 -sky signature -r "Test And Dev Root Authority.cer"
      Enter
      type: makecert -pe -n "CN=[your_server_FQDN]" -ss my -sr LocalMachine -a sha1 -sky exchange -eku 1.3.6.1.5.5.7.3.1 -in "Test And Dev Root Authority" -is MY -ir LocalMachine -sp "Microsoft RSA SChannel Cryptographic Provider" -sy 12 security.cer
      Enter
      type: copy security.cer C:\EMPTYPOP
      Enter
      Since security.cer will now be found in the working directory, the software will allow POP3 connections on SSL Port 995
      Restart the EmptyPOP service under Administrative Tools > Services

- When using a self-created SSL cert some mail clients may display the following warning:

      "The server you are connected to is using a security certification that could not be verified. A certificate chain processed, but terminated in a root certification which is not trusted by the trust provider. Do you want to continue using this server?"
      You can safely answer "Yes" to this warning. This is because a self-created certificate is not one that has been issued by a certificate authority.

- How do I make it so EmptyPOP doesn't send any message?

      Delete the message.txt file from the C:\EMPTYPOP folder
      Ensure that MESSAGE= (blank) in C:\EMPTYPOP\settings.ini
      Restart the EmptyPOP service under Administrative Tools > Services

- How can I customize the message that EmptyPOP sends?

      By default EmptyPOP will use the C:\EMPTYPOP\message.txt file, unless another file is set under MESSAGE= in C:\EMPTYPOP\settings.ini
      1st Line = To: field
      2nd Line = From: field
      3rd Line = Subject: field
      4th Line and anything after = Body of the message

- How can I verify the ports EmptyPOP is listening on?

      Run tcpview.exe and look in the Local Address column for all EmptyPOPsvc.exe processes.

- How can I automatically block POP mail from being used anywhere within my company or ISP?

      On your DNS Redirector server install EmptyPOP.
      In C:\EMPTYPOP\settings.ini configure ListenOnIP= to be the same IP address you specified for BlockedIP= in dnsredir.ini
      Configure ^pop\. and ^pop3\. (or simply pop.gmail.com if that's the only POP3 service you're interested in blocking) as blocked keywords for DNS Redirector.
      Configure an SSL Cert for EmptyPOP (instructions above).
      Anyone trying to configure an email client with a POP3 server (at either port 110 or SSL port 995) will get one message (or none) served by EmptyPOP. You might use this one message to inform users on how to configure IMAP instead, or provide a link to visit webmail.
