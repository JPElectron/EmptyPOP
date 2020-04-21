Imports System.ComponentModel
Imports System.Configuration.Install
Imports System.ServiceProcess
Imports Microsoft.Win32

Public Class ProjectInstaller

    Public Sub New()
        MyBase.New()

        'This call is required by the Component Designer.
        InitializeComponent()

        'After the service is installed, start it
        AddHandler Me.Committed, Sub()
                                     Try
                                         Dim controller As New ServiceController(Me.ServiceInstaller1.ServiceName)
                                         controller.Start()
                                     Catch ex As Exception
                                         'Counldn't start the service for some reason
                                     End Try
                                 End Sub

    End Sub

End Class
