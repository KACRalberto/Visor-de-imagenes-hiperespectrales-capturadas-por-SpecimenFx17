using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class MetadataDialog : Form
    {
        private TextBox _txtVariety = new();
        private TextBox _txtDate = new();
        private TextBox _txtNotes = new();
        private NumericUpDown _nudBrix = new();
        private CheckBox _chkBrix = new();
        private SelectionShape _shape;

        public MetadataDialog(SelectionShape shape)
        {
            _shape = shape;
            Text = "Etiquetado de Datos (Ground Truth)";
            Size = new Size(350, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(24, 24, 36);
            ForeColor = Color.White;
            MaximizeBox = false; MinimizeBox = false;

            int cy = 15;
            AddLabel("Variedad:", cy);
            _txtVariety = new TextBox { Location = new Point(100, cy), Width = 200, Text = shape.Variety, BackColor = Color.FromArgb(40, 40, 55), ForeColor = Color.White };
            Controls.Add(_txtVariety); cy += 35;

            AddLabel("Fecha rec.:", cy);
            _txtDate = new TextBox { Location = new Point(100, cy), Width = 200, Text = shape.Date, BackColor = Color.FromArgb(40, 40, 55), ForeColor = Color.White };
            Controls.Add(_txtDate); cy += 35;

            AddLabel("°Brix Real:", cy);
            _chkBrix = new CheckBox { Location = new Point(100, cy + 2), Width = 20, Checked = shape.MeasuredBrix.HasValue };
            _nudBrix = new NumericUpDown { Location = new Point(125, cy), Width = 175, DecimalPlaces = 1, Minimum = 0, Maximum = 100, Increment = 0.5m, BackColor = Color.FromArgb(40, 40, 55), ForeColor = Color.White, Enabled = _chkBrix.Checked };
            if (shape.MeasuredBrix.HasValue) _nudBrix.Value = (decimal)shape.MeasuredBrix.Value;
            _chkBrix.CheckedChanged += (s, e) => _nudBrix.Enabled = _chkBrix.Checked;
            Controls.Add(_chkBrix); Controls.Add(_nudBrix); cy += 35;

            AddLabel("Notas:", cy);
            _txtNotes = new TextBox { Location = new Point(100, cy), Width = 200, Height = 50, Multiline = true, Text = shape.Notes, BackColor = Color.FromArgb(40, 40, 55), ForeColor = Color.White };
            Controls.Add(_txtNotes); cy += 65;

            var btnOk = new Button { Text = "Guardar", Location = new Point(100, cy), Width = 90, BackColor = Color.FromArgb(40, 100, 50), FlatStyle = FlatStyle.Flat };
            var btnCancel = new Button { Text = "Cancelar", Location = new Point(210, cy), Width = 90, BackColor = Color.FromArgb(100, 40, 40), FlatStyle = FlatStyle.Flat };
            btnOk.Click += BtnOk_Click;
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(btnOk); Controls.Add(btnCancel);
        }

        private void AddLabel(string text, int y) => Controls.Add(new Label { Text = text, Location = new Point(15, y + 2), Width = 80, ForeColor = Color.LightGray });

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            _shape.Variety = _txtVariety.Text;
            _shape.Date = _txtDate.Text;
            _shape.Notes = _txtNotes.Text;
            _shape.MeasuredBrix = _chkBrix.Checked ? (float)_nudBrix.Value : null;
            DialogResult = DialogResult.OK;
        }
    }
}