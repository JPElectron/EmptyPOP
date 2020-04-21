@ECHO OFF
ECHO. 
ECHO. Adding exceptions to Windows Firewall...
ECHO. 
NETSH firewall add allowedprogram c:\emptypop\emptypopsvc.exe "EmptyPOP Service" ENABLE
ECHO. Task complete...
ECHO. 
PAUSE
EXIT