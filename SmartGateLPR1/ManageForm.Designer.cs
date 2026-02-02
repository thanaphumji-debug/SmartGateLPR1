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
            txtRFID_Manage = new TextBox();
            txtPlateInput = new TextBox();
            txtNameInput = new TextBox();
            btnSave = new Button();
            btnDelete = new Button();
            dgvUsers = new DataGridView();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvUsers).BeginInit();
            SuspendLayout();
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(txtRFID_Manage);
            groupBox2.Controls.Add(btnDelete);
            groupBox2.Controls.Add(txtPlateInput);
            groupBox2.Controls.Add(btnSave);
            groupBox2.Controls.Add(txtNameInput);
            groupBox2.Location = new Point(53, 48);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(213, 117);
            groupBox2.TabIndex = 17;
            groupBox2.TabStop = false;
            groupBox2.Text = "ข้อมูล";
            // 
            // txtRFID_Manage
            // 
            txtRFID_Manage.Location = new Point(6, 23);
            txtRFID_Manage.Name = "txtRFID_Manage";
            txtRFID_Manage.PlaceholderText = "เลขทะเบียน";
            txtRFID_Manage.Size = new Size(100, 23);
            txtRFID_Manage.TabIndex = 5;
            // 
            // txtPlateInput
            // 
            txtPlateInput.Location = new Point(6, 52);
            txtPlateInput.Name = "txtPlateInput";
            txtPlateInput.PlaceholderText = "Tag RFID";
            txtPlateInput.Size = new Size(100, 23);
            txtPlateInput.TabIndex = 7;
            // 
            // txtNameInput
            // 
            txtNameInput.Location = new Point(6, 81);
            txtNameInput.Name = "txtNameInput";
            txtNameInput.PlaceholderText = "ชื่อเจ้าของรถ";
            txtNameInput.Size = new Size(100, 23);
            txtNameInput.TabIndex = 8;
            // 
            // btnSave
            // 
            btnSave.Location = new Point(125, 22);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(75, 23);
            btnSave.TabIndex = 21;
            btnSave.Text = "บันทึกข้อมูล";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += btnSave_Click;
            // 
            // btnDelete
            // 
            btnDelete.Location = new Point(125, 52);
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
            Controls.Add(groupBox2);
            Name = "ManageForm";
            Text = "ManageForm";
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvUsers).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox2;
        private TextBox txtRFID_Manage;
        private TextBox txtPlateInput;
        private TextBox txtNameInput;
        private Button btnSave;
        private Button btnDelete;
        private DataGridView dgvUsers;
    }
}