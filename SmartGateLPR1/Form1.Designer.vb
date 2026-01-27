<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        pbCamera1 = New PictureBox()
        btnStartCamera = New Button()
        txtRTSP = New TextBox()
        CType(pbCamera1, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        ' 
        ' pbCamera1
        ' 
        pbCamera1.Location = New Point(98, 62)
        pbCamera1.Name = "pbCamera1"
        pbCamera1.Size = New Size(507, 330)
        pbCamera1.TabIndex = 0
        pbCamera1.TabStop = False
        ' 
        ' btnStartCamera
        ' 
        btnStartCamera.Location = New Point(279, 468)
        btnStartCamera.Name = "btnStartCamera"
        btnStartCamera.Size = New Size(96, 23)
        btnStartCamera.TabIndex = 1
        btnStartCamera.Text = "open cemera"
        btnStartCamera.UseVisualStyleBackColor = True
        ' 
        ' txtRTSP
        ' 
        txtRTSP.Location = New Point(119, 423)
        txtRTSP.Name = "txtRTSP"
        txtRTSP.Size = New Size(448, 23)
        txtRTSP.TabIndex = 2
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1250, 623)
        Controls.Add(txtRTSP)
        Controls.Add(btnStartCamera)
        Controls.Add(pbCamera1)
        Name = "Form1"
        Text = "Form1"
        CType(pbCamera1, ComponentModel.ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents pbCamera1 As PictureBox
    Friend WithEvents btnStartCamera As Button
    Friend WithEvents txtRTSP As TextBox

End Class
