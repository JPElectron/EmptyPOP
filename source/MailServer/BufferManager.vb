Imports System.Net.Sockets

''' <summary>
'''  This class creates a single large buffer which can be divided up 
''' and assigned to SocketAsyncEventArgs objects for use with each 
''' socket I/O operation.  
''' This enables bufffers to be easily reused and guards against 
''' fragmenting heap memory.
'''
''' The operations exposed on the BufferManager class are not thread safe.
''' </summary>
''' <remarks></remarks>
Public Class BufferManager

    Private _numBytes As Integer 'the total number of bytes controlled by the buffer pool
    Private _buffer As Byte() ' the underlying byte array maintained by the Buffer Manager
    Private _freeIndexPool As Stack(Of Integer)
    Private _currentIndex As Integer
    Private _bufferSize As Integer

    Public Sub New(ByVal totalBytes As Integer, ByVal bufferSize As Integer)
        _numBytes = totalBytes
        _currentIndex = 0
        _bufferSize = bufferSize
        _freeIndexPool = New Stack(Of Integer)
    End Sub

    ''' <summary>
    ''' Allocates buffer space used by the buffer pool
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub InitBuffer()
        'create one big large buffer and divide that 
        'out to each SocketAsyncEventArg object
        _buffer = New Byte(_numBytes) {}
    End Sub


    ''' <summary>
    '''  Assigns a buffer from the buffer pool to the 
    '''  specified SocketAsyncEventArgs object
    ''' </summary>
    ''' <remarks>true if the buffer was successfully set, else false</remarks>
    Public Function SetBuffer(ByVal args As SocketAsyncEventArgs) As Boolean
        If (_freeIndexPool.Count > 0) Then
            args.SetBuffer(_buffer, _freeIndexPool.Pop(), _bufferSize)
        Else
            If ((_numBytes - _bufferSize) < _currentIndex) Then Return False

            args.SetBuffer(_buffer, _currentIndex, _bufferSize)
            _currentIndex += _bufferSize
        End If
        Return True
    End Function

    ''' <summary>
    ''' Removes the buffer from a SocketAsyncEventArg object.  
    '''  This frees the buffer back to the buffer pool
    ''' </summary>
    ''' <remarks></remarks>
    Public Sub FreeBuffer(ByVal args As SocketAsyncEventArgs)
        _freeIndexPool.Push(args.Offset)
        args.SetBuffer(Nothing, 0, 0)
    End Sub
End Class
