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
            btnDelete = new Button();
            txtPlateInput = new TextBox();
            btnSave = new Button();
            txtNameInput = new TextBox();
            dgvUsers = new DataGridView();
            btnSelectPath = new Button();
            lblCurrentPath = new Label();
            txtSearch = new TextBox();
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
            groupBox2.Location = new Point(61, 64);
            groupBox2.Margin = new Padding(3, 4, 3, 4);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new Padding(3, 4, 3, 4);
            groupBox2.Size = new Size(243, 156);
            groupBox2.TabIndex = 17;
            groupBox2.TabStop = false;
            groupBox2.Text = "ข้อมูล";
            // 
            // txtRFID_Manage
            // 
            txtRFID_Manage.Location = new Point(7, 31);
            txtRFID_Manage.Margin = new Padding(3, 4, 3, 4);
            txtRFID_Manage.Name = "txtRFID_Manage";
            txtRFID_Manage.PlaceholderText = "เลขทะเบียน";
            txtRFID_Manage.Size = new Size(114, 27);
            txtRFID_Manage.TabIndex = 5;
            // 
            // btnDelete
            // 
            btnDelete.Location = new Point(143, 69);
            btnDelete.Margin = new Padding(3, 4, 3, 4);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new Size(86, 31);
            btnDelete.TabIndex = 22;
            btnDelete.Text = "ลบข้อมูล";
            btnDelete.UseVisualStyleBackColor = true;
            btnDelete.Click += btnDelete_Click;
            // 
            // txtPlateInput
            // 
            txtPlateInput.Location = new Point(7, 69);
            txtPlateInput.Margin = new Padding(3, 4, 3, 4);
            txtPlateInput.Name = "txtPlateInput";
            txtPlateInput.PlaceholderText = "Tag RFID";
            txtPlateInput.Size = new Size(114, 27);
            txtPlateInput.TabIndex = 7;
            // 
            // btnSave
            // 
            btnSave.Location = new Point(143, 29);
            btnSave.Margin = new Padding(3, 4, 3, 4);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(86, 31);
            btnSave.TabIndex = 21;
            btnSave.Text = "บันทึกข้อมูล";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += btnSave_Click;
            // 
            // txtNameInput
            // 
            txtNameInput.Location = new Point(7, 108);
            txtNameInput.Margin = new Padding(3, 4, 3, 4);
            txtNameInput.Name = "txtNameInput";
            txtNameInput.PlaceholderText = "ชื่อเจ้าของรถ";
            txtNameInput.Size = new Size(114, 27);
            txtNameInput.TabIndex = 8;
            // 
            // dgvUsers
            // 
            dgvUsers.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvUsers.Location = new Point(578, 105);
            dgvUsers.Margin = new Padding(3, 4, 3, 4);
            dgvUsers.Name = "dgvUsers";
            dgvUsers.RowHeadersWidth = 51;
            dgvUsers.Size = new Size(608, 373);
            dgvUsers.TabIndex = 23;
            dgvUsers.CellClick += dgvUsers_CellContentClick;
            dgvUsers.CellContentClick += dgvUsers_CellContentClick;
            // 
            // btnSelectPath
            // 
            btnSelectPath.Location = new Point(95, 338);
            btnSelectPath.Name = "btnSelectPath";
            btnSelectPath.Size = new Size(125, 29);
            btnSelectPath.TabIndex = 24;
            btnSelectPath.Text = "เลือกที่เก็บข้อมูล";
            btnSelectPath.UseVisualStyleBackColor = true;
            btnSelectPath.Click += btnSelectPath_Click;
            // 
            // lblCurrentPath
            // 
            lblCurrentPath.AutoSize = true;
            lblCurrentPath.Location = new Point(95, 396);
            lblCurrentPath.Name = "lblCurrentPath";
            lblCurrentPath.Size = new Size(39, 20);
            lblCurrentPath.TabIndex = 25;
            lblCurrentPath.Text = "data";
            // 
            // txtSearch
            // 
            txtSearch.Location = new Point(616, 63);
            txtSearch.Name = "txtSearch";
            txtSearch.PlaceholderText = "ค้นหา";
            txtSearch.Size = new Size(302, 27);
            txtSearch.TabIndex = 26;
            txtSearch.TextChanged += txtSearch_TextChanged;
            // 
            // ManageForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1320, 923);
            Controls.Add(txtSearch);
            Controls.Add(lblCurrentPath);
            Controls.Add(btnSelectPath);
            Controls.Add(dgvUsers);
            Controls.Add(groupBox2);
            Margin = new Padding(3, 4, 3, 4);
            Name = "ManageForm";
            Text = "ManageForm";
            Load += ManageForm_Load_1;
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvUsers).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBox2;
        private TextBox txtRFID_Manage;
        private TextBox txtPlateInput;
        private TextBox txtNameInput;
        private Button btnSave;
        private Button btnDelete;
        private DataGridView dgvUsers;
        private Button btnSelectPath;
        private Label lblCurrentPath;
        private TextBox txtSearch;
    }
}