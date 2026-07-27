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
            txtRfidPort = new TextBox();
            txtRfidIP = new TextBox();
            lblRfidStatus1 = new Label();
            label1 = new Label();
            label2 = new Label();
            btnOpenManage = new Button();
            txtSimulateRFID = new TextBox();
            lblStatus = new Label();
            lblShowPlate = new Label();
            lblShowName = new Label();
            picGate = new PictureBox();
            timerGate = new System.Windows.Forms.Timer(components);
            btnScan = new Button();
            lblResult = new Label();
            groupBox2 = new GroupBox();
            txtRFIDInput2 = new Label();
            lblRfidStatus = new Label();
            groupBox3 = new GroupBox();
            groupBox6 = new GroupBox();
            lblLicensePlate2 = new Label();
            lblLprStatus2 = new Label();
            groupBox5 = new GroupBox();
            lblLicensePlate1 = new Label();
            lblLprStatus1 = new Label();
            groupBox4 = new GroupBox();
            button1 = new Button();
            ((System.ComponentModel.ISupportInitialize)pbCamera1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pbCamera2).BeginInit();
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picGate).BeginInit();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox4.SuspendLayout();
            SuspendLayout();
            // 
            // pbCamera1
            // 
            pbCamera1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            pbCamera1.Location = new Point(820, 71);
            pbCamera1.Margin = new Padding(3, 4, 3, 4);
            pbCamera1.Name = "pbCamera1";
            pbCamera1.Size = new Size(775, 541);
            pbCamera1.SizeMode = PictureBoxSizeMode.Zoom;
            pbCamera1.TabIndex = 0;
            pbCamera1.TabStop = false;
            pbCamera1.Click += pbCamera1_Click;
            // 
            // btnStartCamera
            // 
            btnStartCamera.Location = new Point(753, 16);
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
            txtRTSP.Location = new Point(1521, 889);
            txtRTSP.Margin = new Padding(3, 4, 3, 4);
            txtRTSP.Name = "txtRTSP";
            txtRTSP.PlaceholderText = "ตัวอย่าง rtsp://USERNAME:PASSWORD@IP_ADDRESS/stream1";
            txtRTSP.Size = new Size(47, 27);
            txtRTSP.TabIndex = 2;
            txtRTSP.Visible = false;
            txtRTSP.TextChanged += txtRTSP_TextChanged;
            // 
            // pbCamera2
            // 
            pbCamera2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            pbCamera2.Location = new Point(12, 71);
            pbCamera2.Margin = new Padding(3, 4, 3, 4);
            pbCamera2.Name = "pbCamera2";
            pbCamera2.Size = new Size(787, 541);
            pbCamera2.SizeMode = PictureBoxSizeMode.Zoom;
            pbCamera2.TabIndex = 11;
            pbCamera2.TabStop = false;
            // 
            // txtRTSP2
            // 
            txtRTSP2.ForeColor = Color.Black;
            txtRTSP2.Location = new Point(1510, 928);
            txtRTSP2.Margin = new Padding(3, 4, 3, 4);
            txtRTSP2.Name = "txtRTSP2";
            txtRTSP2.PlaceholderText = "ตัวอย่าง rtsp://USERNAME:PASSWORD@IP_ADDRESS/stream1";
            txtRTSP2.Size = new Size(59, 27);
            txtRTSP2.TabIndex = 12;
            txtRTSP2.Visible = false;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(btnConnectRFID);
            groupBox1.Controls.Add(txtRfidPort);
            groupBox1.Controls.Add(txtRfidIP);
            groupBox1.Location = new Point(1300, 460);
            groupBox1.Margin = new Padding(3, 4, 3, 4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(3, 4, 3, 4);
            groupBox1.Size = new Size(269, 171);
            groupBox1.TabIndex = 13;
            groupBox1.TabStop = false;
            groupBox1.Text = "การเชื่อมต่อ RFID";
            groupBox1.Visible = false;
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
            // lblRfidStatus1
            // 
            lblRfidStatus1.AutoSize = true;
            lblRfidStatus1.ForeColor = Color.Red;
            lblRfidStatus1.Location = new Point(7, 352);
            lblRfidStatus1.Name = "lblRfidStatus1";
            lblRfidStatus1.Size = new Size(128, 20);
            lblRfidStatus1.TabIndex = 2;
            lblRfidStatus1.Text = "สถานะ: ยังไม่เชื่อมต่อ";
            lblRfidStatus1.Click += lblRfidStatus1_Click;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            label1.AutoSize = true;
            label1.Location = new Point(357, 27);
            label1.Name = "label1";
            label1.Size = new Size(68, 20);
            label1.TabIndex = 14;
            label1.Text = "Camera1";
            // 
            // label2
            // 
            label2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            label2.AutoSize = true;
            label2.Location = new Point(1195, 27);
            label2.Name = "label2";
            label2.Size = new Size(68, 20);
            label2.TabIndex = 15;
            label2.Text = "Camera2";
            // 
            // btnOpenManage
            // 
            btnOpenManage.Location = new Point(1425, 983);
            btnOpenManage.Margin = new Padding(3, 4, 3, 4);
            btnOpenManage.Name = "btnOpenManage";
            btnOpenManage.Size = new Size(103, 31);
            btnOpenManage.TabIndex = 16;
            btnOpenManage.Text = "การจัดการ RFID";
            btnOpenManage.UseVisualStyleBackColor = true;
            btnOpenManage.Visible = false;
            btnOpenManage.Click += btnOpenManage_Click;
            // 
            // txtSimulateRFID
            // 
            txtSimulateRFID.Location = new Point(45, 63);
            txtSimulateRFID.Margin = new Padding(3, 4, 3, 4);
            txtSimulateRFID.Name = "txtSimulateRFID";
            txtSimulateRFID.PlaceholderText = "การอนุญาตพิเศษ";
            txtSimulateRFID.Size = new Size(164, 27);
            txtSimulateRFID.TabIndex = 18;
            txtSimulateRFID.TextChanged += txtSimulateRFID_TextChanged;
            txtSimulateRFID.KeyDown += txtSimulateRFID_KeyDown;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(45, 193);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(139, 20);
            lblStatus.TabIndex = 19;
            lblStatus.Text = "รายละเอียดการอนุญาต";
            // 
            // lblShowPlate
            // 
            lblShowPlate.AutoSize = true;
            lblShowPlate.Location = new Point(45, 112);
            lblShowPlate.Name = "lblShowPlate";
            lblShowPlate.Size = new Size(118, 20);
            lblShowPlate.TabIndex = 20;
            lblShowPlate.Text = "เลขทะเบียนในระบบ";
            // 
            // lblShowName
            // 
            lblShowName.AutoSize = true;
            lblShowName.Location = new Point(45, 153);
            lblShowName.Name = "lblShowName";
            lblShowName.Size = new Size(101, 20);
            lblShowName.TabIndex = 21;
            lblShowName.Text = "สิทธิ์การอนุญาต";
            // 
            // picGate
            // 
            picGate.BackColor = Color.Red;
            picGate.Location = new Point(45, 253);
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
            btnScan.Location = new Point(1443, 955);
            btnScan.Margin = new Padding(3, 4, 3, 4);
            btnScan.Name = "btnScan";
            btnScan.Size = new Size(141, 32);
            btnScan.TabIndex = 23;
            btnScan.Text = "ตรวจสอบป้ายทะเบียน";
            btnScan.UseVisualStyleBackColor = true;
            btnScan.Visible = false;
            // 
            // lblResult
            // 
            lblResult.AutoSize = true;
            lblResult.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblResult.Location = new Point(194, 269);
            lblResult.Name = "lblResult";
            lblResult.Size = new Size(175, 33);
            lblResult.TabIndex = 24;
            lblResult.Text = "ผลการอนุญาต";
            lblResult.Click += label3_Click_1;
            // 
            // groupBox2
            // 
            groupBox2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            groupBox2.Controls.Add(txtRFIDInput2);
            groupBox2.Controls.Add(lblRfidStatus);
            groupBox2.Controls.Add(lblRfidStatus1);
            groupBox2.Location = new Point(46, 639);
            groupBox2.Margin = new Padding(3, 4, 3, 4);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new Padding(3, 4, 3, 4);
            groupBox2.Size = new Size(477, 403);
            groupBox2.TabIndex = 26;
            groupBox2.TabStop = false;
            groupBox2.Text = "RFID";
            // 
            // txtRFIDInput2
            // 
            txtRFIDInput2.AutoSize = true;
            txtRFIDInput2.ForeColor = Color.DarkGreen;
            txtRFIDInput2.Location = new Point(48, 73);
            txtRFIDInput2.Name = "txtRFIDInput2";
            txtRFIDInput2.Size = new Size(120, 20);
            txtRFIDInput2.TabIndex = 26;
            txtRFIDInput2.Text = "แสดงเลขแท็ก RFID";
            // 
            // lblRfidStatus
            // 
            lblRfidStatus.AutoSize = true;
            lblRfidStatus.Location = new Point(48, 145);
            lblRfidStatus.Name = "lblRfidStatus";
            lblRfidStatus.Size = new Size(82, 20);
            lblRfidStatus.TabIndex = 25;
            lblRfidStatus.Text = "RFID status";
            // 
            // groupBox3
            // 
            groupBox3.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            groupBox3.Controls.Add(groupBox6);
            groupBox3.Controls.Add(groupBox5);
            groupBox3.Location = new Point(529, 636);
            groupBox3.Margin = new Padding(3, 4, 3, 4);
            groupBox3.Name = "groupBox3";
            groupBox3.Padding = new Padding(3, 4, 3, 4);
            groupBox3.Size = new Size(519, 403);
            groupBox3.TabIndex = 27;
            groupBox3.TabStop = false;
            groupBox3.Text = "LPR";
            groupBox3.Enter += groupBox3_Enter;
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(lblLicensePlate2);
            groupBox6.Controls.Add(lblLprStatus2);
            groupBox6.Location = new Point(270, 28);
            groupBox6.Margin = new Padding(3, 4, 3, 4);
            groupBox6.Name = "groupBox6";
            groupBox6.Padding = new Padding(3, 4, 3, 4);
            groupBox6.Size = new Size(240, 348);
            groupBox6.TabIndex = 34;
            groupBox6.TabStop = false;
            groupBox6.Text = "กล้องตัวที่ 2";
            // 
            // lblLicensePlate2
            // 
            lblLicensePlate2.AutoSize = true;
            lblLicensePlate2.ForeColor = Color.DarkBlue;
            lblLicensePlate2.Location = new Point(40, 88);
            lblLicensePlate2.Name = "lblLicensePlate2";
            lblLicensePlate2.Size = new Size(104, 20);
            lblLicensePlate2.TabIndex = 31;
            lblLicensePlate2.Text = "แสดงเลขทะเบียน";
            // 
            // lblLprStatus2
            // 
            lblLprStatus2.AutoSize = true;
            lblLprStatus2.ForeColor = Color.DarkBlue;
            lblLprStatus2.Location = new Point(40, 224);
            lblLprStatus2.Name = "lblLprStatus2";
            lblLprStatus2.Size = new Size(184, 20);
            lblLprStatus2.TabIndex = 32;
            lblLprStatus2.Text = "สถานะการตรวจจับป้ายทะเบียน";
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(lblLicensePlate1);
            groupBox5.Controls.Add(lblLprStatus1);
            groupBox5.Location = new Point(21, 30);
            groupBox5.Margin = new Padding(3, 4, 3, 4);
            groupBox5.Name = "groupBox5";
            groupBox5.Padding = new Padding(3, 4, 3, 4);
            groupBox5.Size = new Size(229, 348);
            groupBox5.TabIndex = 33;
            groupBox5.TabStop = false;
            groupBox5.Text = "กล้องตัวที่ 1";
            // 
            // lblLicensePlate1
            // 
            lblLicensePlate1.AutoSize = true;
            lblLicensePlate1.ForeColor = Color.DarkBlue;
            lblLicensePlate1.Location = new Point(40, 88);
            lblLicensePlate1.Name = "lblLicensePlate1";
            lblLicensePlate1.Size = new Size(104, 20);
            lblLicensePlate1.TabIndex = 29;
            lblLicensePlate1.Text = "แสดงเลขทะเบียน";
            // 
            // lblLprStatus1
            // 
            lblLprStatus1.AutoSize = true;
            lblLprStatus1.ForeColor = Color.DarkBlue;
            lblLprStatus1.Location = new Point(40, 224);
            lblLprStatus1.Name = "lblLprStatus1";
            lblLprStatus1.Size = new Size(184, 20);
            lblLprStatus1.TabIndex = 28;
            lblLprStatus1.Text = "สถานะการตรวจจับป้ายทะเบียน";
            // 
            // groupBox4
            // 
            groupBox4.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            groupBox4.Controls.Add(button1);
            groupBox4.Controls.Add(txtSimulateRFID);
            groupBox4.Controls.Add(lblShowPlate);
            groupBox4.Controls.Add(lblShowName);
            groupBox4.Controls.Add(lblResult);
            groupBox4.Controls.Add(lblStatus);
            groupBox4.Controls.Add(picGate);
            groupBox4.Location = new Point(1055, 639);
            groupBox4.Margin = new Padding(3, 4, 3, 4);
            groupBox4.Name = "groupBox4";
            groupBox4.Padding = new Padding(3, 4, 3, 4);
            groupBox4.Size = new Size(514, 403);
            groupBox4.TabIndex = 27;
            groupBox4.TabStop = false;
            groupBox4.Text = "การอนุญาตเข้า-ออก";
            groupBox4.Enter += groupBox4_Enter;
            // 
            // button1
            // 
            button1.Location = new Point(321, 100);
            button1.Name = "button1";
            button1.Size = new Size(94, 29);
            button1.TabIndex = 25;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            // 
            // btnDisconnectRFID
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackgroundImageLayout = ImageLayout.Center;
            ClientSize = new Size(1607, 1055);
            Controls.Add(groupBox3);
            Controls.Add(groupBox4);
            Controls.Add(groupBox2);
            Controls.Add(btnOpenManage);
            Controls.Add(label2);
            Controls.Add(btnScan);
            Controls.Add(label1);
            Controls.Add(txtRTSP2);
            Controls.Add(pbCamera2);
            Controls.Add(txtRTSP);
            Controls.Add(btnStartCamera);
            Controls.Add(pbCamera1);
            Controls.Add(groupBox1);
            ForeColor = Color.Black;
            Margin = new Padding(3, 4, 3, 4);
            Name = "btnDisconnectRFID";
            Text = "Form1";
            WindowState = FormWindowState.Minimized;
            Load += btnDisconnectRFID_Load;
            ((System.ComponentModel.ISupportInitialize)pbCamera1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pbCamera2).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picGate).EndInit();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
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
        private TextBox txtSimulateRFID;
        private Label lblStatus;
        private Label lblShowPlate;
        private Label lblShowName;
        private PictureBox picGate;
        private System.Windows.Forms.Timer timerGate;
        private Button btnScan;
        private Label lblResult;
        private Label lblLicensePlate;
        private GroupBox groupBox2;
        private GroupBox groupBox3;
        private GroupBox groupBox4;
        private Label lblLprStatus1;
        private Label label4;
        private Label label3;
        private Label lblRfidStatus;
        private GroupBox groupBox6;
        private Label lblLicensePlate2;
        private Label lblLprStatus2;
        private GroupBox groupBox5;
        private Label lblLicensePlate1;
        private Label txtRFIDInput2;
        private Button button1;
    }
}
