namespace SmartGateLPR1
{
    partial class btnDisconnectRFID
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            pbCamera1 = new PictureBox();
            btnStartCamera = new Button();
            txtRTSP = new TextBox();
            pbCamera2 = new PictureBox();
            txtRTSP2 = new TextBox();
            groupBox1 = new GroupBox();
            btnConnectRFID = new Button();
            lblRfidStatus1 = new Label();
            txtRfidPort = new TextBox();
            txtRfidIP = new TextBox();
            label1 = new Label();
            label2 = new Label();
            btnOpenManage = new Button();
            txtRFIDInput2 = new TextBox();
            txtSimulateRFID = new TextBox();
            lblStatus = new Label();
            lblShowPlate = new Label();
            lblShowName = new Label();
            picGate = new PictureBox();
            timerGate = new System.Windows.Forms.Timer(components);
            btnScan = new Button();
            lblResult = new Label();
            lblLicensePlate = new Label();
            ((System.ComponentModel.ISupportInitialize)pbCamera1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pbCamera2).BeginInit();
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picGate).BeginInit();
            SuspendLayout();
            // 
            // pbCamera1
            // 
            pbCamera1.Location = new Point(34, 71);
            pbCamera1.Margin = new Padding(3, 4, 3, 4);
            pbCamera1.Name = "pbCamera1";
            pbCamera1.Size = new Size(711, 541);
            pbCamera1.SizeMode = PictureBoxSizeMode.Zoom;
            pbCamera1.TabIndex = 0;
            pbCamera1.TabStop = false;
            // 
            // btnStartCamera
            // 
            btnStartCamera.Location = new Point(746, 644);
            btnStartCamera.Margin = new Padding(3, 4, 3, 4);
            btnStartCamera.Name = "btnStartCamera";
            btnStartCamera.Size = new Size(107, 31);
            btnStartCamera.TabIndex = 1;
            btnStartCamera.Text = "Stream camera";
            btnStartCamera.UseVisualStyleBackColor = true;
            btnStartCamera.Visible = false;
            btnStartCamera.Click += btnStartCamera_Click;
            // 
            // txtRTSP
            // 
            txtRTSP.Location = new Point(216, 646);
            txtRTSP.Margin = new Padding(3, 4, 3, 4);
            txtRTSP.Name = "txtRTSP";
            txtRTSP.PlaceholderText = "ตัวอย่าง rtsp://USERNAME:PASSWORD@IP_ADDRESS/stream1";
            txtRTSP.Size = new Size(334, 27);
            txtRTSP.TabIndex = 2;
            txtRTSP.Visible = false;
            txtRTSP.TextChanged += txtRTSP_TextChanged;
            // 
            // pbCamera2
            // 
            pbCamera2.Location = new Point(858, 71);
            pbCamera2.Margin = new Padding(3, 4, 3, 4);
            pbCamera2.Name = "pbCamera2";
            pbCamera2.Size = new Size(711, 541);
            pbCamera2.SizeMode = PictureBoxSizeMode.Zoom;
            pbCamera2.TabIndex = 11;
            pbCamera2.TabStop = false;
            // 
            // txtRTSP2
            // 
            txtRTSP2.ForeColor = Color.Black;
            txtRTSP2.Location = new Point(1042, 644);
            txtRTSP2.Margin = new Padding(3, 4, 3, 4);
            txtRTSP2.Name = "txtRTSP2";
            txtRTSP2.PlaceholderText = "ตัวอย่าง rtsp://USERNAME:PASSWORD@IP_ADDRESS/stream1";
            txtRTSP2.Size = new Size(334, 27);
            txtRTSP2.TabIndex = 12;
            txtRTSP2.Visible = false;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(btnConnectRFID);
            groupBox1.Controls.Add(lblRfidStatus1);
            groupBox1.Controls.Add(txtRfidPort);
            groupBox1.Controls.Add(txtRfidIP);
            groupBox1.Location = new Point(14, 813);
            groupBox1.Margin = new Padding(3, 4, 3, 4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(3, 4, 3, 4);
            groupBox1.Size = new Size(309, 156);
            groupBox1.TabIndex = 13;
            groupBox1.TabStop = false;
            groupBox1.Text = "การเชื่อมต่อ RFID";
            groupBox1.Enter += groupBox1_Enter;
            // 
            // btnConnectRFID
            // 
            btnConnectRFID.Location = new Point(166, 48);
            btnConnectRFID.Margin = new Padding(3, 4, 3, 4);
            btnConnectRFID.Name = "btnConnectRFID";
            btnConnectRFID.Size = new Size(99, 31);
            btnConnectRFID.TabIndex = 3;
            btnConnectRFID.Text = "เชื่อมต่อ RFID";
            btnConnectRFID.UseVisualStyleBackColor = true;
            btnConnectRFID.Click += btnConnectRFID_Click;
            // 
            // lblRfidStatus1
            // 
            lblRfidStatus1.AutoSize = true;
            lblRfidStatus1.ForeColor = Color.Red;
            lblRfidStatus1.Location = new Point(152, 107);
            lblRfidStatus1.Name = "lblRfidStatus1";
            lblRfidStatus1.Size = new Size(128, 20);
            lblRfidStatus1.TabIndex = 2;
            lblRfidStatus1.Text = "สถานะ: ยังไม่เชื่อมต่อ";
            lblRfidStatus1.Click += lblRfidStatus1_Click;
            // 
            // txtRfidPort
            // 
            txtRfidPort.Location = new Point(7, 103);
            txtRfidPort.Margin = new Padding(3, 4, 3, 4);
            txtRfidPort.Name = "txtRfidPort";
            txtRfidPort.PlaceholderText = "Port";
            txtRfidPort.Size = new Size(114, 27);
            txtRfidPort.TabIndex = 1;
            txtRfidPort.TextChanged += txtRfidPort_TextChanged;
            // 
            // txtRfidIP
            // 
            txtRfidIP.Location = new Point(7, 48);
            txtRfidIP.Margin = new Padding(3, 4, 3, 4);
            txtRfidIP.Name = "txtRfidIP";
            txtRfidIP.PlaceholderText = "IP address";
            txtRfidIP.Size = new Size(114, 27);
            txtRfidIP.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(362, 27);
            label1.Name = "label1";
            label1.Size = new Size(68, 20);
            label1.TabIndex = 14;
            label1.Text = "Camera1";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(1191, 27);
            label2.Name = "label2";
            label2.Size = new Size(68, 20);
            label2.TabIndex = 15;
            label2.Text = "Camera2";
            // 
            // btnOpenManage
            // 
            btnOpenManage.Location = new Point(1390, 933);
            btnOpenManage.Margin = new Padding(3, 4, 3, 4);
            btnOpenManage.Name = "btnOpenManage";
            btnOpenManage.Size = new Size(103, 31);
            btnOpenManage.TabIndex = 16;
            btnOpenManage.Text = "การจัดการ RFID";
            btnOpenManage.UseVisualStyleBackColor = true;
            btnOpenManage.Click += btnOpenManage_Click;
            // 
            // txtRFIDInput2
            // 
            txtRFIDInput2.Location = new Point(14, 741);
            txtRFIDInput2.Margin = new Padding(3, 4, 3, 4);
            txtRFIDInput2.Name = "txtRFIDInput2";
            txtRFIDInput2.Size = new Size(271, 27);
            txtRFIDInput2.TabIndex = 17;
            // 
            // txtSimulateRFID
            // 
            txtSimulateRFID.Location = new Point(626, 801);
            txtSimulateRFID.Margin = new Padding(3, 4, 3, 4);
            txtSimulateRFID.Name = "txtSimulateRFID";
            txtSimulateRFID.Size = new Size(164, 27);
            txtSimulateRFID.TabIndex = 18;
            txtSimulateRFID.KeyDown += txtSimulateRFID_KeyDown;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(640, 963);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(50, 20);
            lblStatus.TabIndex = 19;
            lblStatus.Text = "label3";
            // 
            // lblShowPlate
            // 
            lblShowPlate.AutoSize = true;
            lblShowPlate.Location = new Point(640, 859);
            lblShowPlate.Name = "lblShowPlate";
            lblShowPlate.Size = new Size(50, 20);
            lblShowPlate.TabIndex = 20;
            lblShowPlate.Text = "label3";
            // 
            // lblShowName
            // 
            lblShowName.AutoSize = true;
            lblShowName.Location = new Point(640, 901);
            lblShowName.Name = "lblShowName";
            lblShowName.Size = new Size(50, 20);
            lblShowName.TabIndex = 21;
            lblShowName.Text = "label4";
            // 
            // picGate
            // 
            picGate.BackColor = Color.Red;
            picGate.Location = new Point(866, 805);
            picGate.Margin = new Padding(3, 4, 3, 4);
            picGate.Name = "picGate";
            picGate.Size = new Size(114, 67);
            picGate.TabIndex = 22;
            picGate.TabStop = false;
            // 
            // timerGate
            // 
            timerGate.Tick += timerGate_Tick;
            // 
            // btnScan
            // 
            btnScan.Location = new Point(1193, 843);
            btnScan.Margin = new Padding(3, 4, 3, 4);
            btnScan.Name = "btnScan";
            btnScan.Size = new Size(127, 31);
            btnScan.TabIndex = 23;
            btnScan.Text = "ตรวจสอบป้ายทะเบียน";
            btnScan.UseVisualStyleBackColor = true;
            // 
            // lblResult
            // 
            lblResult.AutoSize = true;
            lblResult.Location = new Point(1193, 801);
            lblResult.Name = "lblResult";
            lblResult.Size = new Size(50, 20);
            lblResult.TabIndex = 24;
            lblResult.Text = "label3";
            lblResult.Click += label3_Click_1;
            // 
            // lblLicensePlate
            // 
            lblLicensePlate.AutoSize = true;
            lblLicensePlate.Location = new Point(264, 696);
            lblLicensePlate.Name = "lblLicensePlate";
            lblLicensePlate.Size = new Size(286, 20);
            lblLicensePlate.TabIndex = 25;
            lblLicensePlate.Text = "                            เลขทะเบียน                         \r\n";
            lblLicensePlate.Click += label3_Click_2;
            // 
            // btnDisconnectRFID
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackgroundImageLayout = ImageLayout.Center;
            ClientSize = new Size(1598, 1055);
            Controls.Add(lblLicensePlate);
            Controls.Add(lblResult);
            Controls.Add(btnScan);
            Controls.Add(picGate);
            Controls.Add(lblShowName);
            Controls.Add(lblShowPlate);
            Controls.Add(lblStatus);
            Controls.Add(txtSimulateRFID);
            Controls.Add(txtRFIDInput2);
            Controls.Add(btnOpenManage);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(groupBox1);
            Controls.Add(txtRTSP2);
            Controls.Add(pbCamera2);
            Controls.Add(txtRTSP);
            Controls.Add(btnStartCamera);
            Controls.Add(pbCamera1);
            ForeColor = Color.Black;
            Margin = new Padding(3, 4, 3, 4);
            Name = "btnDisconnectRFID";
            Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)pbCamera1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pbCamera2).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picGate).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private PictureBox pbCamera1;
        private Button btnStartCamera;
        private TextBox txtRTSP;
        private PictureBox pbCamera2;
        private TextBox txtRTSP2;
        private GroupBox groupBox1;
        private TextBox txtRfidPort;
        private TextBox txtRfidIP;
        private Label lblRfidStatus1;
        private Button btnConnectRFID;
        private Label label1;
        private Label label2;
        private Button btnOpenManage;
        private TextBox txtRFIDInput2;
        private TextBox txtSimulateRFID;
        private Label lblStatus;
        private Label lblShowPlate;
        private Label lblShowName;
        private PictureBox picGate;
        private System.Windows.Forms.Timer timerGate;
        private Button btnScan;
        private Label lblResult;
        private Label lblLicensePlate;
    }
}
