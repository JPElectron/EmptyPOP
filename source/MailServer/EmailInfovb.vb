Imports System.IO

''' <summary>
''' An email for the POP3 server to process
''' </summary>
''' <remarks></remarks>
Public Class EmailInfo
    Public Property Size As Long
    Public Property Content As Stream
    Public Property IsDeleted As Boolean
    Public Property ID As String
End Class
