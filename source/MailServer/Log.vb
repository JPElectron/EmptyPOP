
''' <summary>
''' File backed log
''' </summary>
''' <remarks></remarks>
Public Class Log

    Private _logStream As IO.StreamWriter
    Private Delegate Sub WriteEntryDelegate(ByVal entry As String)
    Private _invoker As New WriteEntryDelegate(AddressOf WriteEntry)
    Public Property Enabled As Boolean = True

    ''' <summary>
    ''' </summary>
    ''' <param name="path">Path to the file to log to.
    ''' If the file does not exist it is created, otherwise it is appended to.</param>
    ''' <remarks></remarks>
    Public Sub New(ByVal path As String)
        Try
            _logStream = New IO.StreamWriter(path, True)
            _logStream.AutoFlush = True
        Catch ex As Exception
            _enabled = False
        End Try

    End Sub

    ''' <summary>
    ''' Asyncronously writes to the log
    ''' </summary>
    ''' <param name="entry"></param>
    ''' <remarks></remarks>
    Public Sub BeginWriteEntry(ByVal entry As String)
        If _Enabled Then _invoker.BeginInvoke(entry, Nothing, Nothing)
    End Sub

    ''' <summary>
    ''' Writes to the log.
    ''' </summary>
    ''' <param name="entry"></param>
    ''' <remarks>This is thread safe</remarks>
    Public Sub WriteEntry(ByVal entry As String)
        If _Enabled Then
            SyncLock _logStream
                _logStream.WriteLine("{0} {1}", Now.ToString("MMddyy hh:mm:ss"), entry)
            End SyncLock
        End If
    End Sub

End Class
