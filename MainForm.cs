using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpecimenFX17.Imaging
{
    public class MainForm : Form
    {
        // ── Datos ─────────────────────────────────────────────────────────────
        private HyperspectralCube? _cube;
        private int  _currentBand   = 0;
        private bool _grayscaleMode = false;
        private Bitmap? _currentBitmap;

        // ── Selecciones unificadas ─────────────────────────────────────────────
        private readonly List<SelectionShape> _selections = new();
        private static readonly Color[] SelColors =
        {
            Color.Cyan, Color.Yellow, Color.LimeGreen, Color.OrangeRed,
            Color.Magenta, Color.White, Color.DeepSkyBlue, Color.GreenYellow
        };

        // ── Herramienta activa ─────────────────────────────────────────────────
        private SelectionTool _tool = SelectionTool.Rectangle;
        private Button[] _toolBtns  = null!;
        private Label    _lblTip    = null!;

        // ── Estado arrastre (Rect / Circle / Freehand) ────────────────────────
        private bool  _isDragging;
        private Point _dragStartScr, _dragStartImg, _dragCurScr;

        // ── Estado polígono en construcción ───────────────────────────────────
        private bool        _polyActive;
        private List<Point> _polyImg = new(), _polyScr = new();
        private Point       _polyMouse;

        // ── Estado trazo libre ────────────────────────────────────────────────
        private List<Point> _freeImg = new(), _freeScr = new();

        // ── Controles ─────────────────────────────────────────────────────────
        private PictureBox    _pictureBox   = null!;
        private PictureBox    _specPlot     = null!;
        private Label         _lblCoords    = null!;
        private Label         _lblWl        = null!;
        private Label         _lblSpecInfo  = null!;
        private Label         _lblBandInfo  = null!;
        private TrackBar      _slider       = null!;
        private ComboBox      _cmbCmap      = null!;
        private CheckBox      _chkCbar      = null!;
        private CheckBox      _chkGray      = null!;
        private NumericUpDown _nudGamma     = null!;
        private NumericUpDown _nudLo        = null!;
        private NumericUpDown _nudHi        = null!;
        private NumericUpDown _nudThr       = null!;
        private Button        _btnLoad      = null!;
        private Button        _btnExport    = null!;
        private Button        _btnExpAll    = null!;
        private Button        _btnClear     = null!;
        private ProgressBar   _pb           = null!;
        private StatusStrip   _ss           = null!;
        private ToolStripStatusLabel _slbl  = null!;

        public MainForm()
        {
            Text = "SpecimenFX17 — Visor BLI Hiperespectral";
            Size = new Size(1400, 900); MinimumSize = new Size(1000, 650);
            BackColor = Color.FromArgb(18,18,26); ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            BuildUI();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CONSTRUCCIÓN DE INTERFAZ
        // ═══════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            // Status bar
            _ss   = new StatusStrip { BackColor = Color.FromArgb(12,12,20), SizingGrip=false };
            _slbl = new ToolStripStatusLabel("Carga un archivo .hdr")
                    { ForeColor=Color.FromArgb(100,200,100), Spring=true, TextAlign=ContentAlignment.MiddleLeft };
            _pb   = new ProgressBar { Style=ProgressBarStyle.Continuous, Visible=false, Size=new Size(200,14) };
            _ss.Items.Add(_slbl); _ss.Items.Add(new ToolStripControlHost(_pb));

            // Panel derecho
            var rp = new Panel { Dock=DockStyle.Right, Width=240,
                BackColor=Color.FromArgb(24,24,36), AutoScroll=true, Padding=new Padding(10,8,8,6) };
            BuildRightPanel(rp);

            // Panel central
            var cp = new Panel { Dock=DockStyle.Fill, BackColor=Color.Black };

            var slPan = new Panel { Dock=DockStyle.Top, Height=46, BackColor=Color.FromArgb(22,22,34) };
            _lblWl = new Label { Dock=DockStyle.Bottom, Height=18,
                ForeColor=Color.FromArgb(100,210,255), Font=new Font("Consolas",8.5f,FontStyle.Bold),
                Text="  Carga un archivo .hdr", TextAlign=ContentAlignment.MiddleLeft };
            _slider = new TrackBar { Dock=DockStyle.Fill, Minimum=0, Maximum=0,
                TickStyle=TickStyle.None, BackColor=Color.FromArgb(22,22,34) };
            _slider.Scroll += (_,_) => { _currentBand=_slider.Value; RefreshDisplay(); };
            slPan.Controls.Add(_slider); slPan.Controls.Add(_lblWl);

            var spCon = new Panel { Dock=DockStyle.Bottom, Height=200, BackColor=Color.FromArgb(12,12,20) };
            _lblSpecInfo = new Label { Dock=DockStyle.Top, Height=20,
                BackColor=Color.FromArgb(20,20,32), ForeColor=Color.FromArgb(140,140,190),
                Font=new Font("Segoe UI",8f,FontStyle.Italic),
                Text="  Clic = píxel  •  Arrastre/clics = área  •  Doble clic = limpiar  •  Esc = cancelar polígono",
                TextAlign=ContentAlignment.MiddleLeft };
            _specPlot = new PictureBox { Dock=DockStyle.Fill, BackColor=Color.FromArgb(12,12,20),
                SizeMode=PictureBoxSizeMode.Normal };
            _specPlot.Resize += (_,_) => RedrawSpectrumPlot();
            spCon.Controls.Add(_specPlot); spCon.Controls.Add(_lblSpecInfo);

            var div = new Panel { Dock=DockStyle.Bottom, Height=3, BackColor=Color.FromArgb(50,50,70) };

            _pictureBox = new PictureBox { Dock=DockStyle.Fill, SizeMode=PictureBoxSizeMode.Zoom,
                BackColor=Color.Black, Cursor=Cursors.Cross };
            _pictureBox.MouseMove        += Pic_Move;
            _pictureBox.MouseDown        += Pic_Down;
            _pictureBox.MouseUp          += Pic_Up;
            _pictureBox.Paint            += Pic_Paint;
            _pictureBox.MouseDoubleClick += Pic_DblClick;

            _lblCoords = new Label { AutoSize=false, Size=new Size(360,20), Location=new Point(6,6),
                BackColor=Color.FromArgb(160,0,0,0), ForeColor=Color.FromArgb(200,255,200),
                Font=new Font("Consolas",8f), TextAlign=ContentAlignment.MiddleLeft,
                Padding=new Padding(4,0,0,0) };
            _pictureBox.Controls.Add(_lblCoords);

            cp.Controls.Add(_pictureBox);
            cp.Controls.Add(div);
            cp.Controls.Add(spCon);
            cp.Controls.Add(slPan);

            Controls.Add(cp);
            Controls.Add(rp);
            Controls.Add(_ss);
        }

        private void BuildRightPanel(Panel p)
        {
            int cy = 8;
            _btnLoad = Btn(p,"📂  Cargar .hdr / .raw", ref cy, Color.FromArgb(40,90,140));
            _btnLoad.Click += BtnLoad_Click;

            // ── Herramienta de selección ──────────────────────────────────────
            Sep(p, ref cy); Sec(p,"HERRAMIENTA DE SELECCIÓN", ref cy);

            _lblTip = new Label { Location=new Point(8,cy), Width=210, Height=16,
                ForeColor=Color.FromArgb(120,200,120), Font=new Font("Segoe UI",7f,FontStyle.Italic),
                Text="Arrastra para seleccionar un rectángulo" };
            p.Controls.Add(_lblTip); cy += 20;

            // Botones 2×2
            var grid = new TableLayoutPanel { Location=new Point(8,cy), Width=212, Height=56,
                ColumnCount=2, RowCount=2, BackColor=Color.Transparent };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            var defs = new (string Lbl, SelectionTool Mode, string Tip)[]
            {
                ("▭  Rectángulo", SelectionTool.Rectangle, "Arrastra para seleccionar un rectángulo"),
                ("⬟  Polígono",   SelectionTool.Polygon,   "Clic = vértice  •  Enter / doble clic = cerrar"),
                ("○  Círculo",    SelectionTool.Circle,    "Arrastra desde el centro hacia el borde"),
                ("✏  Lasso",      SelectionTool.Freehand,  "Mantén pulsado y dibuja libremente"),
            };
            _toolBtns = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                var (lbl, mode, tip) = defs[i];
                var tb = new Button { Text=lbl, Dock=DockStyle.Fill, Margin=new Padding(2),
                    FlatStyle=FlatStyle.Flat,
                    BackColor = mode==_tool ? Color.FromArgb(50,110,170) : Color.FromArgb(32,32,48),
                    ForeColor=Color.White, Font=new Font("Segoe UI",7.5f), Cursor=Cursors.Hand,
                    Tag=(mode, tip) };
                tb.FlatAppearance.BorderColor = Color.FromArgb(70,70,100);
                tb.Click += (_,_) => { var (m,t)=((SelectionTool,string))tb.Tag!; SetTool(m,t); };
                grid.Controls.Add(tb, i%2, i/2);
                _toolBtns[i] = tb;
            }
            p.Controls.Add(grid); cy += 62;

            // ── Visualización ─────────────────────────────────────────────────
            Sep(p, ref cy); Sec(p,"VISUALIZACIÓN", ref cy);
            Lbl(p,"Paleta de color:", ref cy);
            _cmbCmap = new ComboBox { Location=new Point(8,cy), Width=210,
                DropDownStyle=ComboBoxStyle.DropDownList,
                BackColor=Color.FromArgb(38,38,55), ForeColor=Color.White, FlatStyle=FlatStyle.Flat };
            _cmbCmap.Items.AddRange(Enum.GetNames(typeof(BliColormap)));
            _cmbCmap.SelectedIndex = 0;
            _cmbCmap.SelectedIndexChanged += (_,_) => RefreshDisplay();
            p.Controls.Add(_cmbCmap); cy += 28;

            _chkCbar = Chk(p,"Mostrar barra de escala", ref cy, true);
            _chkGray = Chk(p,"Modo escala de grises",   ref cy, false);
            _chkCbar.CheckedChanged += (_,_) => RefreshDisplay();
            _chkGray.CheckedChanged += (_,_) => { _grayscaleMode=_chkGray.Checked; _cmbCmap.Enabled=!_grayscaleMode; RefreshDisplay(); };

            // ── Ajustes ───────────────────────────────────────────────────────
            Sep(p, ref cy); Sec(p,"AJUSTES DE IMAGEN", ref cy);
            Lbl(p,"Gamma (1 = lineal):", ref cy); _nudGamma=Num(p,ref cy,1.0m,0.1m,5.0m,0.1m,1);
            Lbl(p,"Percentil bajo (%):", ref cy); _nudLo   =Num(p,ref cy,2m,  0m,  49m, 1m,  0);
            Lbl(p,"Percentil alto (%):", ref cy); _nudHi   =Num(p,ref cy,98m, 51m, 100m,1m,  0);
            Lbl(p,"Umbral de señal:",    ref cy); _nudThr  =Num(p,ref cy,0m,  0m,  9999999m,1m,0);
            foreach (var n in new[]{_nudGamma,_nudLo,_nudHi,_nudThr})
                n.ValueChanged += (_,_) => RefreshDisplay();

            // ── Exportar ──────────────────────────────────────────────────────
            Sep(p, ref cy); Sec(p,"EXPORTAR", ref cy);
            _btnExport = Btn(p,"💾  Exportar banda actual",     ref cy, Color.FromArgb(35,95,55));
            _btnExpAll = Btn(p,"📦  Exportar todas las bandas", ref cy, Color.FromArgb(30,75,45));
            _btnClear  = Btn(p,"🗑️  Limpiar selecciones",       ref cy, Color.FromArgb(110,40,40));
            _btnExport.Enabled=_btnExpAll.Enabled=_btnClear.Enabled=false;
            _btnExport.Click += BtnExport_Click;
            _btnExpAll.Click += BtnExportAll_Click;
            _btnClear.Click  += (_,_) => ClearAll();

            // ── Calculadora ───────────────────────────────────────────────────
            Sep(p, ref cy); Sec(p,"CALCULADORA ESPECTRAL", ref cy);
            var bc = Btn(p,"🧮  Abrir calculadora", ref cy, Color.FromArgb(70,45,110));
            bc.Click += (_,_) => {
                if (_cube == null) { MessageBox.Show("Carga un cubo primero.","Aviso"); return; }
                new SpectralCalculatorForm(_cube, _selections.AsReadOnly()).Show();
            };

            // ── Análisis Avanzado ─────────────────────────────────────────────
            Sep(p, ref cy); Sec(p, "ANÁLISIS AVANZADO", ref cy);
            var ba = Btn(p, "🔬  Herramientas Avanzadas", ref cy, Color.FromArgb(140, 70, 45));
            ba.Click += (_, _) => {
                if (_cube == null) { MessageBox.Show("Carga un cubo primero.", "Aviso"); return; }
                new AdvancedAnalysisForm(_cube, _selections.AsReadOnly()).Show();
            };

            // ── Predicción PLS (Brix) ─────────────────────────────────────────
            Sep(p, ref cy); Sec(p, "MODELOS DE PREDICCIÓN", ref cy);
            var bp = Btn(p, "🍊  Predecir °Brix (PLS)", ref cy, Color.FromArgb(140, 90, 30));
            bp.Click += (_, _) => {
                if (_cube == null) { MessageBox.Show("Carga un cubo primero.", "Aviso"); return; }
                new PlsPredictionForm(_cube, _selections.AsReadOnly()).Show();
            };

            // ── Info de banda ─────────────────────────────────────────────────
            Sep(p, ref cy); Sec(p,"INFO DE BANDA", ref cy);
            _lblBandInfo = new Label { Location=new Point(8,cy), Width=210, Height=110,
                ForeColor=Color.FromArgb(160,160,190), Font=new Font("Consolas",7.5f), Text="—" };
            p.Controls.Add(_lblBandInfo);
        }

        // ── Helpers de layout ─────────────────────────────────────────────────
        private Button Btn(Panel p, string t, ref int cy, Color bg)
        {
            var b = new Button { Text=t, Location=new Point(8,cy), Width=210, Height=30,
                FlatStyle=FlatStyle.Flat, BackColor=bg, ForeColor=Color.White, Cursor=Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(
                Math.Min(255,bg.R+35), Math.Min(255,bg.G+35), Math.Min(255,bg.B+35));
            p.Controls.Add(b); cy+=36; return b;
        }
        private NumericUpDown Num(Panel p, ref int cy, decimal v, decimal mn, decimal mx, decimal inc, int dec)
        {
            var n = new NumericUpDown { Location=new Point(8,cy), Width=210,
                Minimum=mn, Maximum=mx, Value=v, Increment=inc, DecimalPlaces=dec,
                BackColor=Color.FromArgb(36,36,52), ForeColor=Color.White };
            p.Controls.Add(n); cy+=26; return n;
        }
        private void Lbl(Panel p, string t, ref int cy)
        {
            p.Controls.Add(new Label { Text=t, Location=new Point(8,cy), Width=210, Height=16,
                ForeColor=Color.FromArgb(140,140,170), Font=new Font("Segoe UI",8f) });
            cy+=17;
        }
        private void Sec(Panel p, string t, ref int cy)
        {
            p.Controls.Add(new Label { Text=t, Location=new Point(8,cy), Width=210, Height=18,
                ForeColor=Color.FromArgb(100,160,220), Font=new Font("Segoe UI",7.5f,FontStyle.Bold),
                TextAlign=ContentAlignment.MiddleLeft });
            cy+=20;
        }
        private CheckBox Chk(Panel p, string t, ref int cy, bool v)
        {
            var c = new CheckBox { Text=t, Location=new Point(8,cy), Width=210, Checked=v,
                ForeColor=Color.FromArgb(180,180,210), BackColor=Color.Transparent };
            p.Controls.Add(c); cy+=24; return c;
        }
        private void Sep(Panel p, ref int cy)
        {
            p.Controls.Add(new Label { Location=new Point(8,cy), Width=210, Height=1,
                BackColor=Color.FromArgb(55,55,75) });
            cy+=10;
        }

        // ── Cambio de herramienta ─────────────────────────────────────────────
        private void SetTool(SelectionTool mode, string tip)
        {
            if (_polyActive) { _polyActive=false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate(); }
            _tool = mode;
            _lblTip.Text = tip;
            for (int i=0; i<_toolBtns.Length; i++)
            {
                var (m,_) = ((SelectionTool,string))_toolBtns[i].Tag!;
                _toolBtns[i].BackColor = m==mode ? Color.FromArgb(50,110,170) : Color.FromArgb(32,32,48);
            }
            _pictureBox.Cursor = mode switch
            {
                SelectionTool.Polygon  => Cursors.UpArrow,
                SelectionTool.Freehand => Cursors.UpArrow,
                _                      => Cursors.Cross
            };
        }

        // Esc = cancelar polígono, Enter = cerrar polígono
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Escape && _polyActive)
            {
                _polyActive=false; _polyImg.Clear(); _polyScr.Clear();
                _pictureBox.Invalidate(); _slbl.Text="Polígono cancelado"; return true;
            }
            if (key == Keys.Return && _polyActive && _polyImg.Count >= 3)
            { CommitPolygon(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  EVENTOS DE RATÓN
        // ═══════════════════════════════════════════════════════════════════

        private void Pic_Down(object? s, MouseEventArgs e)
        {
            if (_cube==null || e.Button!=MouseButtons.Left) return;
            var pt = MapToImage(e.Location); if (pt==null) return;

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                case SelectionTool.Circle:
                    _isDragging=true; _dragStartScr=e.Location;
                    _dragStartImg=pt.Value; _dragCurScr=e.Location; break;

                case SelectionTool.Freehand:
                    _isDragging=true; _freeImg.Clear(); _freeScr.Clear();
                    _freeImg.Add(pt.Value); _freeScr.Add(e.Location); break;

                case SelectionTool.Polygon:
                    if (!_polyActive) { _polyActive=true; _polyImg.Clear(); _polyScr.Clear(); }
                    _polyImg.Add(pt.Value); _polyScr.Add(e.Location); _polyMouse=e.Location;
                    _slbl.Text=$"Polígono: {_polyImg.Count} vértice(s)  —  Enter o doble clic para cerrar  •  Esc para cancelar";
                    _pictureBox.Invalidate(); break;
            }
        }

        private void Pic_Move(object? s, MouseEventArgs e)
        {
            if (_cube==null) return;
            var pt = MapToImage(e.Location);
            if (pt!=null)
            {
                int x=pt.Value.X, y=pt.Value.Y;
                if (x>=0 && x<_cube.Samples && y>=0 && y<_cube.Lines)
                {
                    float v=_cube[_currentBand,y,x]; double wl=WlAt(_currentBand);
                    _lblCoords.Text=$"  X:{x}  Y:{y}  │  λ={wl:F1} nm  │  val={v:G5}";
                    if (!_polyActive)
                        _slbl.Text=$"Píxel ({x},{y})  banda {_currentBand+1}/{_cube.Bands}  λ={wl:F1} nm  val={v:G6}";
                }
                else _lblCoords.Text="";
            }

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                case SelectionTool.Circle:
                    if (_isDragging) { _dragCurScr=e.Location; _pictureBox.Invalidate(); } break;

                case SelectionTool.Polygon:
                    _polyMouse=e.Location; if (_polyActive) _pictureBox.Invalidate(); break;

                case SelectionTool.Freehand:
                    if (_isDragging && pt!=null)
                    {
                        var last = _freeScr.Count>0 ? _freeScr[^1] : e.Location;
                        if (Math.Abs(e.X-last.X)+Math.Abs(e.Y-last.Y)>2)
                        { _freeImg.Add(pt.Value); _freeScr.Add(e.Location); _pictureBox.Invalidate(); }
                    }
                    break;
            }
        }

        private void Pic_Up(object? s, MouseEventArgs e)
        {
            if (_cube==null || e.Button!=MouseButtons.Left) return;
            var endImg = MapToImage(e.Location);

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                {
                    if (!_isDragging) break; _isDragging=false; _pictureBox.Invalidate();
                    if (endImg==null) break;
                    int dx=Math.Abs(endImg.Value.X-_dragStartImg.X);
                    int dy=Math.Abs(endImg.Value.Y-_dragStartImg.Y);
                    Color col=NextColor();
                    if (dx<4 && dy<4)
                    {
                        int x=_dragStartImg.X, y=_dragStartImg.Y;
                        if (x<0||x>=_cube.Samples||y<0||y>=_cube.Lines) break;
                        AddShape(new PixelShape(new Point(x,y), col));
                    }
                    else
                    {
                        int x1=Math.Clamp(Math.Min(_dragStartImg.X,endImg.Value.X),0,_cube.Samples-1);
                        int y1=Math.Clamp(Math.Min(_dragStartImg.Y,endImg.Value.Y),0,_cube.Lines-1);
                        int x2=Math.Clamp(Math.Max(_dragStartImg.X,endImg.Value.X),0,_cube.Samples-1);
                        int y2=Math.Clamp(Math.Max(_dragStartImg.Y,endImg.Value.Y),0,_cube.Lines-1);
                        AddShape(new RectShape(new Rectangle(x1,y1,x2-x1,y2-y1), col));
                    }
                    break;
                }
                case SelectionTool.Circle:
                {
                    if (!_isDragging) break; _isDragging=false; _pictureBox.Invalidate();
                    if (endImg==null) break;
                    int r=(int)Math.Round(Math.Sqrt(
                        Math.Pow(endImg.Value.X-_dragStartImg.X,2)+
                        Math.Pow(endImg.Value.Y-_dragStartImg.Y,2)));
                    if (r<2) break;
                    AddShape(new CircleShape(_dragStartImg, r, NextColor()));
                    break;
                }
                case SelectionTool.Freehand:
                {
                    if (!_isDragging) break; _isDragging=false; _pictureBox.Invalidate();
                    if (_freeImg.Count>=3)
                        AddShape(new FreehandShape(_freeImg, NextColor()));
                    _freeImg.Clear(); _freeScr.Clear(); break;
                }
                // Polygon: vértices se añaden en MouseDown, se cierra con dblclick/Enter
            }
        }

        private void Pic_DblClick(object? s, MouseEventArgs e)
        {
            if (_tool==SelectionTool.Polygon && _polyActive)
            {
                // El 2º MouseDown del doble-clic ya añadió un vértice extra → quitarlo
                if (_polyImg.Count>0) { _polyImg.RemoveAt(_polyImg.Count-1); _polyScr.RemoveAt(_polyScr.Count-1); }
                CommitPolygon();
            }
            else ClearAll();
        }

        // ── Paint: ghost de la selección en progreso ──────────────────────────
        private void Pic_Paint(object? s, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            var col = SelColors[_selections.Count % SelColors.Length];

            switch (_tool)
            {
                case SelectionTool.Rectangle:
                {
                    if (!_isDragging) break;
                    int x1=Math.Min(_dragStartScr.X,_dragCurScr.X), y1=Math.Min(_dragStartScr.Y,_dragCurScr.Y);
                    int w=Math.Abs(_dragCurScr.X-_dragStartScr.X),  h=Math.Abs(_dragCurScr.Y-_dragStartScr.Y);
                    if (w<2||h<2) break;
                    DrawGhostRect(g,x1,y1,w,h,col);
                    var si=MapToImage(_dragStartScr); var ci=MapToImage(_dragCurScr);
                    if (si!=null&&ci!=null)
                        GhostLbl(g,$"{Math.Abs(ci.Value.X-si.Value.X)} × {Math.Abs(ci.Value.Y-si.Value.Y)} px",
                            x1+w/2f, y1+h/2f, col);
                    break;
                }
                case SelectionTool.Circle:
                {
                    if (!_isDragging) break;
                    float r=(float)Math.Sqrt(Math.Pow(_dragCurScr.X-_dragStartScr.X,2)+Math.Pow(_dragCurScr.Y-_dragStartScr.Y,2));
                    if (r<2) break;
                    float cx=_dragStartScr.X, cy=_dragStartScr.Y;
                    using var fill=new SolidBrush(Color.FromArgb(30,col.R,col.G,col.B));
                    g.FillEllipse(fill,cx-r,cy-r,r*2,r*2);
                    using var pen=new Pen(col,1.5f){DashStyle=DashStyle.Dash};
                    g.DrawEllipse(pen,cx-r,cy-r,r*2,r*2);
                    using var cp=new Pen(col,2f);
                    g.DrawLine(cp,cx-7,cy,cx+7,cy); g.DrawLine(cp,cx,cy-7,cx,cy+7);
                    using var rp=new Pen(col,1f){DashStyle=DashStyle.Dot};
                    g.DrawLine(rp,cx,cy,cx+r,cy);
                    // radio en px de imagen
                    var si=MapToImage(_dragStartScr); var ci=MapToImage(_dragCurScr);
                    if (si!=null&&ci!=null)
                    {
                        int imgR=(int)Math.Round(Math.Sqrt(Math.Pow(ci.Value.X-si.Value.X,2)+Math.Pow(ci.Value.Y-si.Value.Y,2)));
                        GhostLbl(g,$"r = {imgR} px", cx+r*0.55f, cy-r*0.75f, col);
                    }
                    break;
                }
                case SelectionTool.Polygon:
                {
                    if (!_polyActive||_polyScr.Count==0) break;
                    var all=_polyScr.Concat(new[]{_polyMouse}).Select(p=>(PointF)p).ToArray();
                    if (all.Length>=3)
                    {
                        using var fill=new SolidBrush(Color.FromArgb(22,col.R,col.G,col.B));
                        g.FillPolygon(fill,all);
                    }
                    using var lp=new Pen(col,1.5f){DashStyle=DashStyle.Dash};
                    for (int i=0;i<_polyScr.Count-1;i++) g.DrawLine(lp,_polyScr[i],_polyScr[i+1]);
                    g.DrawLine(lp,_polyScr[^1],_polyMouse);
                    if (_polyScr.Count>=2)
                    {
                        using var cl=new Pen(Color.FromArgb(90,col.R,col.G,col.B),1f){DashStyle=DashStyle.Dot};
                        g.DrawLine(cl,_polyMouse,_polyScr[0]);
                    }
                    foreach (var v in _polyScr)
                    {
                        using var vb=new SolidBrush(Color.FromArgb(190,0,0,0));
                        g.FillEllipse(vb,v.X-4,v.Y-4,8,8);
                        using var vp=new Pen(col,1.5f); g.DrawEllipse(vp,v.X-4,v.Y-4,8,8);
                    }
                    Crosshair(g,_polyMouse,col);
                    GhostLbl(g,$"{_polyScr.Count} vértices",_polyMouse.X+12,_polyMouse.Y-14,col);
                    break;
                }
                case SelectionTool.Freehand:
                {
                    if (!_isDragging||_freeScr.Count<2) break;
                    var pts=_freeScr.Select(p=>(PointF)p).ToArray();
                    if (pts.Length>=3)
                    {
                        using var fill=new SolidBrush(Color.FromArgb(22,col.R,col.G,col.B));
                        g.FillPolygon(fill,pts);
                    }
                    using var pen=new Pen(col,1.5f); g.DrawLines(pen,pts);
                    using var cl=new Pen(Color.FromArgb(90,col.R,col.G,col.B),1f){DashStyle=DashStyle.Dot};
                    g.DrawLine(cl,pts[^1],pts[0]);
                    break;
                }
            }
        }

        // ── Ghost helpers ─────────────────────────────────────────────────────
        private static void DrawGhostRect(Graphics g, int x, int y, int w, int h, Color col)
        {
            using var fill=new SolidBrush(Color.FromArgb(30,col.R,col.G,col.B)); g.FillRectangle(fill,x,y,w,h);
            using var pen=new Pen(col,1.5f){DashStyle=DashStyle.Dash};            g.DrawRectangle(pen,x,y,w,h);
            int c=7; using var cp=new Pen(col,2.5f);
            g.DrawLine(cp,x,y,x+c,y);     g.DrawLine(cp,x,y,x,y+c);
            g.DrawLine(cp,x+w,y,x+w-c,y); g.DrawLine(cp,x+w,y,x+w,y+c);
            g.DrawLine(cp,x,y+h,x+c,y+h); g.DrawLine(cp,x,y+h,x,y+h-c);
            g.DrawLine(cp,x+w,y+h,x+w-c,y+h); g.DrawLine(cp,x+w,y+h,x+w,y+h-c);
        }
        private static void GhostLbl(Graphics g, string t, float x, float y, Color col)
        {
            using var f=new Font("Consolas",8f); using var bg=new SolidBrush(Color.FromArgb(150,0,0,0));
            using var br=new SolidBrush(col);
            var sz=g.MeasureString(t,f); x-=sz.Width/2;
            g.FillRectangle(bg,x-1,y-1,sz.Width+2,sz.Height+2); g.DrawString(t,f,br,x,y);
        }
        private static void Crosshair(Graphics g, Point p, Color col)
        {
            using var pen=new Pen(col,2f);
            g.DrawLine(pen,p.X-7,p.Y,p.X+7,p.Y); g.DrawLine(pen,p.X,p.Y-7,p.X,p.Y+7);
        }

        // ── Gestión de selecciones ────────────────────────────────────────────
        private Color NextColor() => SelColors[_selections.Count % SelColors.Length];

        private void AddShape(SelectionShape sh)
        {
            if (_selections.Count >= SelColors.Length) _selections.RemoveAt(0);
            _selections.Add(sh);
            _btnClear.Enabled = true;
            DrawMarkers(); RedrawSpectrumPlot(); _pictureBox.Invalidate();
        }

        private void CommitPolygon()
        {
            if (_polyImg.Count<3) { _slbl.Text="Polígono necesita al menos 3 vértices"; }
            else { AddShape(new PolygonShape(_polyImg, NextColor())); _slbl.Text=$"✔ Polígono {_polyImg.Count} vértices añadido"; }
            _polyActive=false; _polyImg.Clear(); _polyScr.Clear(); _pictureBox.Invalidate();
        }

        private void ClearAll()
        {
            _selections.Clear(); _polyActive=false;
            _polyImg.Clear(); _polyScr.Clear(); _freeImg.Clear(); _freeScr.Clear();
            _btnClear.Enabled=false; ClearSpectrumPlot(); _pictureBox.Invalidate();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CARGA DEL CUBO
        // ═══════════════════════════════════════════════════════════════════

        private async void BtnLoad_Click(object? s, EventArgs e)
        {
            using var dlg=new OpenFileDialog
            { Title="Abrir imagen hiperespectral ENVI", Filter="ENVI Header (*.hdr)|*.hdr|Datos RAW (*.raw)|*.raw|Todos|*.*" };
            if (dlg.ShowDialog()!=DialogResult.OK) return;
            await LoadCubeAsync(dlg.FileName);
        }

        private async Task LoadCubeAsync(string path)
        {
            _btnLoad.Enabled=false; _pb.Visible=true; _pb.Value=0;
            _slbl.Text="Cargando cubo hiperespectral…";
            var prog=new Progress<int>(v=>{ _pb.Value=v; _slbl.Text=$"Cargando… {v} %"; });
            try
            {
                _cube=await Task.Run(()=>HyperspectralCube.Load(path,prog));
                _selections.Clear();
                _slider.Minimum=0; _slider.Maximum=Math.Max(0,_cube.Bands-1); _slider.Value=0; _currentBand=0;
                _btnExport.Enabled=_btnExpAll.Enabled=true;
                RefreshDisplay(); ClearSpectrumPlot();
                _slbl.Text=$"✔  {_cube.Header}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n\n{ex.Message}","Error de carga",MessageBoxButtons.OK,MessageBoxIcon.Error);
                _slbl.Text="Error de carga";
            }
            finally { _pb.Visible=false; _btnLoad.Enabled=true; }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  RENDERIZADO
        // ═══════════════════════════════════════════════════════════════════

        private void RefreshDisplay()
        {
            if (_cube==null) return;
            RenderClean();
            if (_selections.Count>0) { DrawMarkers(); RedrawSpectrumPlot(); }
        }

        private void RenderClean()
        {
            if (_cube==null) return;
            var opts=BuildOptions(); if (_grayscaleMode) opts.Colormap=BliColormap.Grayscale;
            _currentBitmap?.Dispose();
            _currentBitmap=BliRenderer.RenderBand(_cube,_currentBand,opts);
            _pictureBox.Image=_currentBitmap;
            UpdateBandLbls();
        }

        private void UpdateBandLbls()
        {
            if (_cube==null) return;
            double wl=WlAt(_currentBand);
            _lblWl.Text=$"  Banda {_currentBand+1} / {_cube.Bands}   │   λ = {wl:F2} {_cube.Header.WavelengthUnits}   │   {_cube.Samples} × {_cube.Lines} px";
            var (mn,mx)=_cube.GetBandStats(_currentBand);
            _lblBandInfo.Text=$"Banda:  {_currentBand+1}\nλ:      {wl:F2} {_cube.Header.WavelengthUnits}\nMín:    {mn:G5}\nMáx:    {mx:G5}\nAncho:  {_cube.Samples} px\nAlto:   {_cube.Lines} px";
        }

        private void DrawMarkers()
        {
            if (_currentBitmap==null||_cube==null) return;
            using var g=Graphics.FromImage(_currentBitmap);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            foreach (var sh in _selections) sh.DrawOn(g);
            _pictureBox.Image=_currentBitmap;
        }

        private void ClearSpectrumPlot()
        {
            _specPlot.Image?.Dispose(); _specPlot.Image=null;
            if (_cube!=null) RenderClean();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  GRÁFICO ESPECTRAL
        // ═══════════════════════════════════════════════════════════════════

        private void RedrawSpectrumPlot()
        {
            if (_cube==null||_selections.Count==0) return;
            int w=Math.Max(_specPlot.Width,300), h=Math.Max(_specPlot.Height,80);
            var bmp=new Bitmap(w,h);
            using var g=Graphics.FromImage(bmp);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(12,12,20));

            const int pL=64,pR=20,pT=24,pB=40;
            var plot=new Rectangle(pL,pT,w-pL-pR,h-pT-pB);
            if (plot.Width<20||plot.Height<10) { _specPlot.Image=bmp; return; }

            // Rango Y
            float yMin=float.MaxValue, yMax=float.MinValue;
            foreach (var sh in _selections)
            { foreach (float v in sh.GetSpectrum(_cube)) { if(v<yMin)yMin=v; if(v>yMax)yMax=v; } }
            if (yMin==float.MaxValue){yMin=0;yMax=1;}
            float yRng=yMax-yMin; if(yRng<1e-10f)yRng=1f;
            yMin-=yRng*0.05f; yMax+=yRng*0.05f; yRng=yMax-yMin;

            // Rango X
            var wls=_cube.Header.Wavelengths;
            double xMin=wls.Count>0?wls[0]:0, xMax=wls.Count>0?wls[^1]:_cube.Bands-1;
            double xRng=xMax-xMin; if(xRng<1e-10)xRng=1;

            // Grid
            using (var gp=new Pen(Color.FromArgb(28,255,255,255),1f){DashStyle=DashStyle.Dot})
            {
                for (int i=0;i<=5;i++) g.DrawLine(gp,plot.Left,plot.Bottom-(float)i/5*plot.Height,plot.Right,plot.Bottom-(float)i/5*plot.Height);
                for (int i=0;i<=6;i++) g.DrawLine(gp,plot.Left+(float)i/6*plot.Width,plot.Top,plot.Left+(float)i/6*plot.Width,plot.Bottom);
            }
            using (var bp=new Pen(Color.FromArgb(65,255,255,255))) g.DrawRectangle(bp,plot);

            // Línea de banda actual
            double curWl=WlAt(_currentBand); float curPx=Px(curWl,xMin,xRng,plot);
            using (var dp=new Pen(Color.FromArgb(110,255,255,80),1f){DashStyle=DashStyle.Dash})
                g.DrawLine(dp,curPx,plot.Top,curPx,plot.Bottom);

            // Curvas
            foreach (var sh in _selections)
            {
                var spec=sh.GetSpectrum(_cube);
                bool solid=(sh is PixelShape);
                DrawCurve(g,spec,wls,plot,xMin,xRng,yMin,yRng,sh.Color,dashed:!solid);
                float val=spec[Math.Clamp(_currentBand,0,spec.Length-1)];
                float dpx=Px(curWl,xMin,xRng,plot), dpy=Py(val,yMin,yRng,plot);
                if (solid)
                {
                    using var fb=new SolidBrush(sh.Color); g.FillEllipse(fb,dpx-5,dpy-5,10,10);
                    using var ep=new Pen(Color.White,1f);  g.DrawEllipse(ep,dpx-5,dpy-5,10,10);
                }
                else
                {
                    using var fb=new SolidBrush(sh.Color); g.FillRectangle(fb,dpx-4,dpy-4,8,8);
                    using var ep=new Pen(Color.White,1f);  g.DrawRectangle(ep,dpx-4,dpy-4,8,8);
                }
            }

            // Ejes
            using var tf=new Font("Consolas",7.5f); using var tb=new SolidBrush(Color.FromArgb(160,160,195));
            using var af=new Font("Segoe UI",8f,FontStyle.Italic); using var ab=new SolidBrush(Color.FromArgb(120,130,165));
            for (int i=0;i<=7;i++)
            {
                double wl=xMin+xRng*i/7; float px=plot.Left+(float)(i/7.0*plot.Width);
                string lb=wl>=1000?$"{wl/1000:F1}µ":$"{wl:F0}";
                var sz=g.MeasureString(lb,tf); g.DrawString(lb,tf,tb,px-sz.Width/2,plot.Bottom+2);
            }
            var xts=g.MeasureString($"Longitud de onda ({_cube.Header.WavelengthUnits})",af);
            g.DrawString($"Longitud de onda ({_cube.Header.WavelengthUnits})",af,ab,plot.Left+plot.Width/2f-xts.Width/2f,plot.Bottom+20);
            for (int i=0;i<=5;i++)
            {
                float v=yMin+yRng*i/5, py=plot.Bottom-(float)i/5*plot.Height;
                string lb=FmtVal(v); var sz=g.MeasureString(lb,tf);
                g.DrawString(lb,tf,tb,plot.Left-sz.Width-3,py-sz.Height/2);
            }
            var st=g.Save(); g.TranslateTransform(10,plot.Top+plot.Height/2f); g.RotateTransform(-90);
            g.DrawString("Intensidad / Reflectancia",af,ab,-72,-6); g.Restore(st);

            // Leyenda
            using var lf=new Font("Consolas",7.5f); int lx=plot.Right-144, ly=plot.Top+4;
            foreach (var sh in _selections)
            {
                using var lpen=new Pen(sh.Color,2f); if(sh is not PixelShape) lpen.DashStyle=DashStyle.Dash;
                using var lb2=new SolidBrush(sh.Color);
                g.DrawLine(lpen,lx,ly+5,lx+16,ly+5);
                g.DrawString($"{sh.LegendIcon} {sh.ShortLabel}",lf,lb2,lx+20,ly); ly+=14;
            }

            using var tf2=new Font("Segoe UI",8.5f,FontStyle.Bold);
            using var tb2=new SolidBrush(Color.FromArgb(170,180,215));
            g.DrawString("Espectro de reflectancia / intensidad",tf2,tb2,pL,4);

            _specPlot.Image?.Dispose(); _specPlot.Image=bmp;

            double wlI=WlAt(_currentBand);
            string sel=string.Join("  +  ",_selections.GroupBy(sh=>sh.GetType().Name)
                .Select(gr=>$"{gr.Count()} {gr.Key.Replace("Shape","")}"));
            _lblSpecInfo.Text=$"  {sel}   │   banda {_currentBand+1}/{_cube.Bands}  λ = {wlI:F1} nm   │  Doble clic para limpiar";
        }

        private static void DrawCurve(Graphics g, float[] spec, List<double> wls,
            Rectangle plot, double xMin, double xRng, float yMin, float yRng, Color col, bool dashed)
        {
            if (spec.Length<2) return;
            var pts=new PointF[spec.Length];
            for (int i=0;i<spec.Length;i++)
            {
                double wl=i<wls.Count?wls[i]:xMin+i*xRng/Math.Max(1,spec.Length-1);
                pts[i]=new PointF(Px(wl,xMin,xRng,plot),Py(spec[i],yMin,yRng,plot));
            }
            if (!dashed)
            {
                var fill=new PointF[pts.Length+2];
                fill[0]=new PointF(pts[0].X,plot.Bottom); pts.CopyTo(fill,1); fill[^1]=new PointF(pts[^1].X,plot.Bottom);
                using var fb=new SolidBrush(Color.FromArgb(20,col.R,col.G,col.B)); g.FillPolygon(fb,fill);
            }
            using var sh=new Pen(Color.FromArgb(45,col.R,col.G,col.B),3.5f); g.DrawLines(sh,pts);
            using var lp=new Pen(col,1.8f){LineJoin=LineJoin.Round}; if(dashed) lp.DashStyle=DashStyle.Dash;
            g.DrawLines(lp,pts);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  EXPORTAR
        // ═══════════════════════════════════════════════════════════════════

        private void BtnExport_Click(object? s, EventArgs e)
        {
            if (_currentBitmap==null) return;
            using var dlg=new SaveFileDialog
            { Filter="PNG (*.png)|*.png|TIFF (*.tif)|*.tif|BMP (*.bmp)|*.bmp",
              FileName=$"BLI_banda{_currentBand+1:D3}_{WlAt(_currentBand):F1}nm" };
            if (dlg.ShowDialog()==DialogResult.OK) _currentBitmap.Save(dlg.FileName,GetFmt(dlg.FileName));
        }

        private async void BtnExportAll_Click(object? s, EventArgs e)
        {
            if (_cube==null) return;
            using var dlg=new FolderBrowserDialog{Description="Carpeta de salida"};
            if (dlg.ShowDialog()!=DialogResult.OK) return;
            _btnExpAll.Enabled=false;
            var opts=BuildOptions(); if(_grayscaleMode) opts.Colormap=BliColormap.Grayscale;
            await Task.Run(()=>{
                for (int b=0;b<_cube.Bands;b++)
                {
                    opts.Wavelength=WlAt(b);
                    using var bmp=BliRenderer.RenderBand(_cube,b,opts);
                    bmp.Save(Path.Combine(dlg.SelectedPath,$"banda_{b+1:D3}_{opts.Wavelength:F1}nm.png"),ImageFormat.Png);
                    Invoke(()=>{ _pb.Visible=true; _pb.Value=(b+1)*100/_cube.Bands; _slbl.Text=$"Exportando {b+1}/{_cube.Bands}…"; });
                }
            });
            _pb.Visible=false; _slbl.Text=$"✔ {_cube.Bands} bandas exportadas"; _btnExpAll.Enabled=true;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  UTILIDADES
        // ═══════════════════════════════════════════════════════════════════

        private BliRenderOptions BuildOptions() => new()
        {
            Colormap=(BliColormap)_cmbCmap.SelectedIndex, Gamma=(float)_nudGamma.Value,
            LowPercentile=(float)_nudLo.Value, HighPercentile=(float)_nudHi.Value,
            SignalThreshold=(float)_nudThr.Value, DrawColorbar=_chkCbar.Checked,
            Wavelength=WlAt(_currentBand), WavelengthUnit=_cube?.Header.WavelengthUnits??"nm"
        };

        private double WlAt(int b)
        {
            if (_cube==null) return double.NaN;
            return b<_cube.Header.Wavelengths.Count ? _cube.Header.Wavelengths[b] : b;
        }

        private Point? MapToImage(Point sc)
        {
            if (_currentBitmap==null) return null;
            float scale=Math.Max((float)_currentBitmap.Width/_pictureBox.Width,
                                 (float)_currentBitmap.Height/_pictureBox.Height);
            float ox=(_pictureBox.Width -_currentBitmap.Width /scale)/2f;
            float oy=(_pictureBox.Height-_currentBitmap.Height/scale)/2f;
            return new Point((int)((sc.X-ox)*scale),(int)((sc.Y-oy)*scale));
        }

        private static float Px(double wl,double xMin,double xRng,Rectangle p)=>p.Left+(float)((wl-xMin)/xRng*p.Width);
        private static float Py(float v,float yMin,float yRng,Rectangle p)=>
            Math.Clamp(p.Bottom-(v-yMin)/yRng*p.Height,p.Top-8,p.Bottom+8);
        private static ImageFormat GetFmt(string p)=>Path.GetExtension(p).ToLower() switch
        { ".tif" or ".tiff"=>ImageFormat.Tiff, ".bmp"=>ImageFormat.Bmp, _=>ImageFormat.Png };
        private static string FmtVal(float v)=>
            Math.Abs(v)>=10000||(Math.Abs(v)<0.001f&&v!=0)?v.ToString("0.0e0"):v.ToString("G4");
    }
}
