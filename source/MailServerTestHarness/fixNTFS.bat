@ECHO OFF
ECHO. 
ECHO. Adding NTFS permissions...
ECHO. 
ECHO y| CACLS c:\emptypop /T /C /G BUILTIN\Administrators:F "NT AUTHORITY\LOCAL SERVICE":F "NT AUTHORITY\SYSTEM":F
ECHO. 
ECHO. Task complete, verify permissions below...
ECHO. 
CACLS c:\emptypop
PAUSE
EXIT