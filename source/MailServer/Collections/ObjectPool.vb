Imports System.Collections.Concurrent

Namespace Collections

    ''' <summary>
    ''' Thread safe pool of objects
    ''' </summary>
    ''' <typeparam name="T"></typeparam>
    ''' <remarks></remarks>
    Public Class ObjectPool(Of T)
        Private _objects As ConcurrentBag(Of T)
        Private _objectGenerator As Func(Of T)

        ''' <summary>
        ''' </summary>
        ''' <param name="objectGenerator">The function that will generate a new instance of an object for use in the pool</param>
        ''' <remarks></remarks>
        Public Sub New(ByVal objectGenerator As Func(Of T))
            If objectGenerator Is Nothing Then Throw New ArgumentNullException("objectGenerator")
            _objects = New ConcurrentBag(Of T)()
            _objectGenerator = objectGenerator
        End Sub

        ''' <summary>
        ''' </summary>
        ''' <param name="objectGenerator">The function that will generate a new instance of an object for use in the pool</param>
        ''' <param name="initialObjectCount">Will initially create this many objects in the pool</param>
        ''' <remarks></remarks>
        Public Sub New(ByVal objectGenerator As Func(Of T), ByVal initialObjectCount As String)
            Me.New(objectGenerator)

            For i As Integer = 1 To initialObjectCount
                PutObject(_objectGenerator())
            Next

        End Sub

        Public Function GetObject() As T
            Dim item As T
            If _objects.TryTake(item) Then Return item
            Return _objectGenerator()
        End Function

        Public Sub PutObject(ByVal item As T)
            _objects.Add(item)
        End Sub

    End Class
End Namespace
