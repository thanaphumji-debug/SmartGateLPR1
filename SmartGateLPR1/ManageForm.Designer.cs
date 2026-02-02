namespace SmartGateLPR1
{
    partial class ManageForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            groupBox2 = new GroupBox();
            btnTestAddData = new Button();
            btnCheckData = new Button();
            txtPlateInput = new TextBox();
            txtRFIDInput = new TextBox();
            txtNameInput = new TextBox();
            ทะเบียน = new TextBox();
            textBox2 = new TextBox();
            textBox3 = new TextBox();
            btnSave = new Button();
            btnDelete = new Button();
            dgvUsers = new DataGridView();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvUsers).BeginInit();
            SuspendLayout();
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(btnTestAddData);
            groupBox2.Controls.Add(btnCheckData);
            groupBox2.Controls.Add(txtPlateInput);
            groupBox2.Controls.Add(txtRFIDInput);
            groupBox2.Controls.Add(txtNameInput);
            groupBox2.Location = new Point(52, 540);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(213, 117);
            groupBox2.TabIndex = 17;
            groupBox2.TabStop = false;
            groupBox2.Text = "ข้อมูล";
            // 
            // btnTestAddData
            // 
            btnTestAddData.Location = new Point(125, 22);
            btnTestAddData.Name = "btnTestAddData";
            btnTestAddData.Size = new Size(75, 23);
            btnTestAddData.TabIndex = 3;
            btnTestAddData.Text = "เพิ่มข้อมูล";
            btnTestAddData.UseVisualStyleBackColor = true;
            // 
            // btnCheckData
            // 
            btnCheckData.Location = new Point(125, 51);
            btnCheckData.Name = "btnCheckData";
            btnCheckData.Size = new Size(75, 23);
            btnCheckData.TabIndex = 4;
            btnCheckData.Text = "เช็คสิทธิ์";
            btnCheckData.UseVisualStyleBackColor = true;
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
            // ทะเบียน
            // 
            ทะเบียน.Location = new Point(52, 32);
            ทะเบียน.Name = "ทะเบียน";
            ทะเบียน.Size = new Size(231, 23);
            ทะเบียน.TabIndex = 18;
            // 
            // textBox2
            // 
            textBox2.Location = new Point(52, 70);
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(231, 23);
            textBox2.TabIndex = 19;
            // 
            // textBox3
            // 
            textBox3.Location = new Point(52, 112);
            textBox3.Name = "textBox3";
            textBox3.Size = new Size(231, 23);
            textBox3.TabIndex = 20;
            // 
            // btnSave
            // 
            btnSave.Location = new Point(63, 179);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(75, 23);
            btnSave.TabIndex = 21;
            btnSave.Text = "บันทึกข้อมูล";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += btnSave_Click;
            // 
            // btnDelete
            // 
            btnDelete.Location = new Point(177, 179);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new Size(75, 23);
            btnDelete.TabIndex = 22;
            btnDelete.Text = "ลบข้อมูล";
            btnDelete.UseVisualStyleBackColor = true;
            btnDelete.Click += btnDelete_Click;
            // 
            // dgvUsers
            // 
            dgvUsers.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvUsers.Location = new Point(501, 32);
            dgvUsers.Name = "dgvUsers";
            dgvUsers.Size = new Size(532, 280);
            dgvUsers.TabIndex = 23;
            dgvUsers.CellClick += dgvUsers_CellContentClick;
            dgvUsers.CellContentClick += dgvUsers_CellContentClick;
            // 
            // ManageForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1155, 692);
            Controls.Add(dgvUsers);
            Controls.Add(btnDelete);
            Controls.Add(btnSave);
            Controls.Add(textBox3);
            Controls.Add(textBox2);
            Controls.Add(ทะเบียน);
            Controls.Add(groupBox2);
            Name = "ManageForm";
            Text = "ManageForm";
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvUsers).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBox2;
        private Button btnTestAddData;
        private Button btnCheckData;
        private TextBox txtPlateInput;
        private TextBox txtRFIDInput;
        private TextBox txtNameInput;
        private TextBox ทะเบียน;
        private TextBox textBox2;
        private TextBox textBox3;
        private Button btnSave;
        private Button btnDelete;
        private DataGridView dgvUsers;
    }
}