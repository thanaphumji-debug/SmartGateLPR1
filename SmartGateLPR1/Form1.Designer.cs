namespace SmartGateLPR1
{
    partial class Form1
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
            pbCamera1 = new PictureBox();
            btnStartCamera = new Button();
            txtRTSP = new TextBox();
            btnTestAddData = new Button();
            btnCheckData = new Button();
            txtPlateInput = new TextBox();
            txtRFIDInput = new TextBox();
            txtNameInput = new TextBox();
            pbCamera2 = new PictureBox();
            txtRTSP2 = new TextBox();
            groupBox1 = new GroupBox();
            btnConnectRFID = new Button();
            lblRfidStatus1 = new Label();
            txtRfidPort = new TextBox();
            txtRfidIP = new TextBox();
            label1 = new Label();
            label2 = new Label();
            groupBox2 = new GroupBox();
            ((System.ComponentModel.ISupportInitialize)pbCamera1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pbCamera2).BeginInit();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // pbCamera1
            // 
            pbCamera1.Location = new Point(30, 53);
            pbCamera1.Name = "pbCamera1";
            pbCamera1.Size = new Size(622, 406);
            pbCamera1.SizeMode = PictureBoxSizeMode.Zoom;
            pbCamera1.TabIndex = 0;
            pbCamera1.TabStop = false;
            // 
            // btnStartCamera
            // 
            btnStartCamera.Location = new Point(653, 483);
            btnStartCamera.Name = "btnStartCamera";
            btnStartCamera.Size = new Size(94, 23);
            btnStartCamera.TabIndex = 1;
            btnStartCamera.Text = "Stream camera";
            btnStartCamera.UseVisualStyleBackColor = true;
            btnStartCamera.Click += btnStartCamera_Click;
            // 
            // txtRTSP
            // 
            txtRTSP.Location = new Point(182, 483);
            txtRTSP.Name = "txtRTSP";
            txtRTSP.PlaceholderText = "ตัวอย่าง rtsp://USERNAME:PASSWORD@IP_ADDRESS/stream1";
            txtRTSP.Size = new Size(293, 23);
            txtRTSP.TabIndex = 2;
            // 
            // btnTestAddData
            // 
            btnTestAddData.Location = new Point(125, 22);
            btnTestAddData.Name = "btnTestAddData";
            btnTestAddData.Size = new Size(75, 23);
            btnTestAddData.TabIndex = 3;
            btnTestAddData.Text = "เพิ่มข้อมูล";
            btnTestAddData.UseVisualStyleBackColor = true;
            btnTestAddData.Click += btnTestAddData_Click_1;
            // 
            // btnCheckData
            // 
            btnCheckData.Location = new Point(125, 51);
            btnCheckData.Name = "btnCheckData";
            btnCheckData.Size = new Size(75, 23);
            btnCheckData.TabIndex = 4;
            btnCheckData.Text = "เช็คสิทธิ์";
            btnCheckData.UseVisualStyleBackColor = true;
            btnCheckData.Click += btnCheckData_Click_1;
            // 
            // txtPlateInput
            // 
            txtPlateInput.Location = new Point(6, 23);
            txtPlateInput.Name = "txtPlateInput";
            txtPlateInput.PlaceholderText = "เลขทะเบียน";
            txtPlateInput.Size = new Size(100, 23);
            txtPlateInput.TabIndex = 5;
            // 
            // txtRFIDInput
            // 
            txtRFIDInput.Location = new Point(6, 52);
            txtRFIDInput.Name = "txtRFIDInput";
            txtRFIDInput.PlaceholderText = "Tag RFID";
            txtRFIDInput.Size = new Size(100, 23);
            txtRFIDInput.TabIndex = 7;
            // 
            // txtNameInput
            // 
            txtNameInput.Location = new Point(6, 81);
            txtNameInput.Name = "txtNameInput";
            txtNameInput.PlaceholderText = "ชื่อเจ้าของรถ";
            txtNameInput.Size = new Size(100, 23);
            txtNameInput.TabIndex = 8;
            // 
            // pbCamera2
            // 
            pbCamera2.Location = new Point(751, 53);
            pbCamera2.Name = "pbCamera2";
            pbCamera2.Size = new Size(622, 406);
            pbCamera2.SizeMode = PictureBoxSizeMode.Zoom;
            pbCamera2.TabIndex = 11;
            pbCamera2.TabStop = false;
            // 
            // txtRTSP2
            // 
            txtRTSP2.ForeColor = Color.Black;
            txtRTSP2.Location = new Point(912, 483);
            txtRTSP2.Name = "txtRTSP2";
            txtRTSP2.PlaceholderText = "ตัวอย่าง rtsp://USERNAME:PASSWORD@IP_ADDRESS/stream1";
            txtRTSP2.Size = new Size(293, 23);
            txtRTSP2.TabIndex = 12;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(btnConnectRFID);
            groupBox1.Controls.Add(lblRfidStatus1);
            groupBox1.Controls.Add(txtRfidPort);
            groupBox1.Controls.Add(txtRfidIP);
            groupBox1.Location = new Point(12, 610);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(270, 117);
            groupBox1.TabIndex = 13;
            groupBox1.TabStop = false;
            groupBox1.Text = "การเชื่อมต่อ RFID";
            groupBox1.Enter += groupBox1_Enter;
            // 
            // btnConnectRFID
            // 
            btnConnectRFID.Location = new Point(145, 36);
            btnConnectRFID.Name = "btnConnectRFID";
            btnConnectRFID.Size = new Size(87, 23);
            btnConnectRFID.TabIndex = 3;
            btnConnectRFID.Text = "เชื่อมต่อ RFID";
            btnConnectRFID.UseVisualStyleBackColor = true;
            btnConnectRFID.Click += btnConnectRFID_Click;
            // 
            // lblRfidStatus1
            // 
            lblRfidStatus1.AutoSize = true;
            lblRfidStatus1.ForeColor = Color.Red;
            lblRfidStatus1.Location = new Point(133, 80);
            lblRfidStatus1.Name = "lblRfidStatus1";
            lblRfidStatus1.Size = new Size(99, 15);
            lblRfidStatus1.TabIndex = 2;
            lblRfidStatus1.Text = "สถานะ: ยังไม่เชื่อมต่อ";
            lblRfidStatus1.Click += lblRfidStatus1_Click;
            // 
            // txtRfidPort
            // 
            txtRfidPort.Location = new Point(6, 77);
            txtRfidPort.Name = "txtRfidPort";
            txtRfidPort.PlaceholderText = "Port";
            txtRfidPort.Size = new Size(100, 23);
            txtRfidPort.TabIndex = 1;
            txtRfidPort.TextChanged += txtRfidPort_TextChanged;
            // 
            // txtRfidIP
            // 
            txtRfidIP.Location = new Point(6, 36);
            txtRfidIP.Name = "txtRfidIP";
            txtRfidIP.PlaceholderText = "IP address";
            txtRfidIP.Size = new Size(100, 23);
            txtRfidIP.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(317, 20);
            label1.Name = "label1";
            label1.Size = new Size(54, 15);
            label1.TabIndex = 14;
            label1.Text = "Camera1";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(1042, 20);
            label2.Name = "label2";
            label2.Size = new Size(54, 15);
            label2.TabIndex = 15;
            label2.Text = "Camera2";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(btnTestAddData);
            groupBox2.Controls.Add(btnCheckData);
            groupBox2.Controls.Add(txtPlateInput);
            groupBox2.Controls.Add(txtRFIDInput);
            groupBox2.Controls.Add(txtNameInput);
            groupBox2.Location = new Point(369, 610);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(213, 117);
            groupBox2.TabIndex = 16;
            groupBox2.TabStop = false;
            groupBox2.Text = "ข้อมูล";
            groupBox2.Enter += groupBox2_Enter;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackgroundImageLayout = ImageLayout.Center;
            ClientSize = new Size(1398, 814);
            Controls.Add(groupBox2);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(groupBox1);
            Controls.Add(txtRTSP2);
            Controls.Add(pbCamera2);
            Controls.Add(txtRTSP);
            Controls.Add(btnStartCamera);
            Controls.Add(pbCamera1);
            ForeColor = Color.Black;
            Name = "Form1";
            Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)pbCamera1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pbCamera2).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private PictureBox pbCamera1;
        private Button btnStartCamera;
        private TextBox txtRTSP;
        private Button btnTestAddData;
        private Button btnCheckData;
        private TextBox txtPlateInput;
        private TextBox txtRFIDInput;
        private TextBox txtNameInput;
        private PictureBox pbCamera2;
        private TextBox txtRTSP2;
        private GroupBox groupBox1;
        private TextBox txtRfidPort;
        private TextBox txtRfidIP;
        private Label lblRfidStatus1;
        private Button btnConnectRFID;
        private Label label1;
        private Label label2;
        private GroupBox groupBox2;
    }
}
