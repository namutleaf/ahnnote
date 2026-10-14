// AHNNOTE - 가벼운 노트 앱
// Windows 11 기본 포함 .NET Framework 4.x 의 csc.exe 로 빌드 (BUILD.BAT 실행)
// 메모장으로 저장할 때 인코딩은 UTF-8 로 저장하세요.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AhnNote
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // BUILD.BAT 이 아이콘 파일을 만들 때 사용
            if (args.Length > 0 && args[0].ToLower() == "/makeicon")
            {
                try { AppIcon.WriteIco(Path.Combine(Application.StartupPath, "AHNNOTE.ico")); } catch { }
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // RichEdit 직접 호출용 (형광펜 없애기)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CHARFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public uint dwEffects;
        public int yHeight;
        public int yOffset;
        public int crTextColor;
        public byte bCharSet;
        public byte bPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szFaceName;
        public short wWeight;
        public short sSpacing;
        public int crBackColor;
        public int lcid;
        public int dwReserved;
        public short sStyle;
        public short wKerning;
        public byte bUnderlineType;
        public byte bAnimation;
        public byte bRevAuthor;
        public byte bReserved1;
    }

    public class MainForm : Form
    {
        // ---------- 상수 ----------
        const string DATA_FOLDER = "AhnNoteData";
        const string TRASH = "_휴지통";
        const string FONT_NAME = "Malgun Gothic";
        const string SYM_FONT = "Segoe UI Symbol";
        const float BASE_SIZE = 11f;
        const string BOX = "☐";
        const string BOX_ON = "☑";
        const string BULLET = "• ";



        static readonly Color[] NB_COLORS = {
            Color.FromArgb(0, 103, 192), Color.FromArgb(11, 107, 203), Color.FromArgb(15, 123, 108),
            Color.FromArgb(217, 115, 13), Color.FromArgb(224, 62, 62), Color.FromArgb(105, 64, 165),
            Color.FromArgb(0, 137, 123), Color.FromArgb(194, 24, 91) };

        // ---------- 테마 (라이트 / 다크) ----------
        bool dark = false;
        Color cAccent, cLogo, cWin, cSide, cList, cSel, cText, cMuted, cLine, cBar;
        List<Panel> sidePanels = new List<Panel>();
        List<Label> sideHeads = new List<Label>();
        List<Button> sideAdds = new List<Button>();
        Label logo;
        Panel titleLine;
        ToolStripButton darkBtn, topBtn;
        ToolStripDropDownButton snipBtn;
        bool topMost = false;
        int moleBest = 0;
        Dictionary<string, string> nbColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Timer holdTimer;
        MoleGame mole;
        CalData cal;
        CalendarForm calForm;
        Timer alarmTimer;
        Button calBtn;
        // 즐겨찾기 / 포스트잇
        List<string> favs = new List<string>();      // "노트북\t페이지"
        Panel favPanel;
        Label favHead;
        ListBox favList;
        ContextMenuStrip favMenu;
        List<StickyNote> stickies = new List<StickyNote>();
        bool appClosing = false;
        Panel lockBar;
        Label lockLabel;
        Button lockShowBtn, lockBackBtn;
        int dragIndex = -1;
        Point dragStart;
        string dragPage;
        bool detachNow = false;
        Point detachAt;

        // ---------- Win32 ----------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        static extern IntPtr SendCharFormat(IntPtr h, int msg, IntPtr w, ref CHARFORMAT2 cf);
        const int WM_SETREDRAW = 0x000B;
        const int EM_SETCHARFORMAT = 0x0444;
        const int SCF_SELECTION = 0x0001;
        const uint CFM_BACKCOLOR = 0x04000000;
        const int EM_GETCHARFORMAT = 0x043A;
        const uint CFM_COLOR = 0x40000000;
        const uint CFE_AUTOCOLOR = 0x40000000;

        // ---------- 화면 ----------
        float S = 1f;
        ToolStrip tool;
        ToolStripButton boldBtn, italicBtn, underBtn, strikeBtn;
        ToolStripComboBox sizeBox;
        ToolStripSplitButton foreBtn, hiBtn;
        ToolStripDropDownButton bgBtn, headBtn;
        StatusStrip status;
        ToolStripStatusLabel stLeft, stRight;
        SplitContainer split1, split2;
        ListBox nbList, pgList;
        Panel pagePanel;
        TextBox titleBox;
        Label dateLabel;
        NoteEditor ed;
        ContextMenuStrip nbMenu, pgMenu, edMenu;
        ToolStripMenuItem moveToMenu, favMenuItem, stickyMenuItem;
        Timer saveTimer;
        FindForm findForm;

        // ---------- 데이터 ----------
        string dataDir;
        string curNotebook;
        string curPage;
        List<string> nbOrder = new List<string>();
        List<string> pgOrder = new List<string>();
        Dictionary<string, string> pgBg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Font baseFont;
        Font favSmallFont = new Font("Malgun Gothic", 8f);
        bool dirty { get { return ed.Dirty; } set { ed.Dirty = value; } }
        bool loading { get { return ed.Loading; } set { ed.Loading = value; } }
        bool listBusy = false;
        bool uiBusy = false;
        string lastSaved = "";

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) { S = g.DpiX / 96f; }
            dataDir = Path.Combine(Application.StartupPath, DATA_FOLDER);
            if (!CanWrite(dataDir))
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AHNNOTE");
            Directory.CreateDirectory(dataDir);
            LoadSettings();
            cal = new CalData(dataDir);
            cal.Load();
            Snippets.FilePath = Path.Combine(dataDir, "_snippets.txt");
            Snippets.Load();
            baseFont = new Font(FONT_NAME, BASE_SIZE);
            Text = "AHNNOTE";
            Font = new Font(FONT_NAME, 9f);
            Size = new Size(Px(1200), Px(780));
            StartPosition = FormStartPosition.CenterScreen;
            Icon = MakeIcon();

            BuildEditor();
            BuildLists();
            BuildToolbar();
            BuildStatus();
            BuildMenus();

            split2 = new SplitContainer();
            split2.Dock = DockStyle.Fill;
            split2.FixedPanel = FixedPanel.Panel1;
            split2.SplitterWidth = Px(3);
            split2.Panel1.Controls.Add(MakeSidePanel("페이지", pgList, "+ 페이지 추가", new EventHandler(NewPage_Click)));
            split2.Panel2.Controls.Add(pagePanel);

            split1 = new SplitContainer();
            split1.Dock = DockStyle.Fill;
            split1.FixedPanel = FixedPanel.Panel1;
            split1.SplitterWidth = Px(3);
            Panel nbPanel = MakeSidePanel("노트북", nbList, "+ 노트북 추가", new EventHandler(NewNotebook_Click));
            logo = new Label();
            logo.Text = "  AHNNOTE";
            logo.Dock = DockStyle.Top;
            logo.Height = Px(46);
            logo.TextAlign = ContentAlignment.MiddleLeft;
            logo.Font = new Font(FONT_NAME, 14f, FontStyle.Bold);
            logo.MouseDown += new MouseEventHandler(Logo_MouseDown);
            logo.MouseUp += new MouseEventHandler(Logo_MouseUp);
            logo.MouseMove += new MouseEventHandler(Logo_MouseMove);
            logo.MouseLeave += new EventHandler(Logo_MouseLeave);
            holdTimer = new Timer();
            holdTimer.Interval = 3000;
            holdTimer.Tick += new EventHandler(HoldTimer_Tick);
            calBtn = new Button();
            calBtn.Text = "  ▦  캘린더";
            calBtn.Dock = DockStyle.Top;
            calBtn.Height = Px(40);
            calBtn.FlatStyle = FlatStyle.Flat;
            calBtn.FlatAppearance.BorderSize = 0;
            calBtn.TextAlign = ContentAlignment.MiddleLeft;
            calBtn.Font = new Font(FONT_NAME, 10f, FontStyle.Bold);
            calBtn.Cursor = Cursors.Hand;
            calBtn.TabStop = false;
            calBtn.Click += new EventHandler(CalBtn_Click);
            BuildFavorites();
            nbPanel.Controls.Add(favPanel);
            nbPanel.Controls.Add(calBtn);
            nbPanel.Controls.Add(logo);
            split1.Panel1.Controls.Add(nbPanel);
            split1.Panel2.Controls.Add(split2);

            Controls.Add(split1);
            Controls.Add(tool);
            Controls.Add(status);
            ApplyTheme();

            saveTimer = new Timer();
            saveTimer.Interval = 3000;
            saveTimer.Tick += new EventHandler(SaveTimer_Tick);
            saveTimer.Start();
        }

        int Px(int v) { return (int)(v * S); }

        // ================= 화면 구성 =================

        void BuildEditor()
        {
            ed = new NoteEditor(baseFont, S);
            ed.Dock = DockStyle.Fill;
            ed.SelectionChanged += new EventHandler(Ed_SelectionChanged);
            ed.FormatChanged += new EventHandler(Ed_FormatChanged);

            titleBox = new TextBox();
            titleBox.Dock = DockStyle.Top;
            titleBox.BorderStyle = BorderStyle.None;
            titleBox.Font = new Font(FONT_NAME, 20f);
            titleBox.KeyDown += new KeyEventHandler(TitleBox_KeyDown);
            titleBox.Leave += new EventHandler(TitleBox_Leave);

            titleLine = new Panel();
            titleLine.Dock = DockStyle.Top;
            titleLine.Height = 1;

            dateLabel = new Label();
            dateLabel.Dock = DockStyle.Top;
            dateLabel.Height = Px(50);
            dateLabel.TextAlign = ContentAlignment.MiddleLeft;

            pagePanel = new Panel();
            pagePanel.Dock = DockStyle.Fill;
            pagePanel.BackColor = Color.White;
            pagePanel.Padding = new Padding(Px(40), Px(20), Px(16), Px(6));
            lockBar = new Panel();
            lockBar.Dock = DockStyle.Top;
            lockBar.Height = Px(40);
            lockBar.Visible = false;
            lockBar.Padding = new Padding(Px(10), Px(5), Px(6), Px(5));
            lockLabel = new Label();
            lockLabel.Dock = DockStyle.Fill;
            lockLabel.TextAlign = ContentAlignment.MiddleLeft;
            lockLabel.Text = "포스트잇 창에서 편집 중인 페이지입니다. 여기서는 읽기만 됩니다.";
            lockBackBtn = new Button();
            lockBackBtn.Text = "메인으로 가져오기";
            lockBackBtn.Dock = DockStyle.Right;
            lockBackBtn.Width = Px(130);
            lockBackBtn.Click += new EventHandler(LockBack_Click);
            lockShowBtn = new Button();
            lockShowBtn.Text = "포스트잇 보기";
            lockShowBtn.Dock = DockStyle.Right;
            lockShowBtn.Width = Px(110);
            lockShowBtn.Click += new EventHandler(LockShow_Click);
            lockBar.Controls.Add(lockLabel);
            lockBar.Controls.Add(lockShowBtn);
            lockBar.Controls.Add(lockBackBtn);

            pagePanel.Controls.Add(ed);
            pagePanel.Controls.Add(lockBar);
            pagePanel.Controls.Add(dateLabel);
            pagePanel.Controls.Add(titleLine);
            pagePanel.Controls.Add(titleBox);
        }

        void BuildLists()
        {
            nbList = MakeList();
            nbList.SelectedIndexChanged += new EventHandler(NbList_SelectedIndexChanged);
            nbList.KeyDown += new KeyEventHandler(NbList_KeyDown);
            nbList.MouseDoubleClick += new MouseEventHandler(NbList_DoubleClick);

            pgList = MakeList();
            pgList.SelectedIndexChanged += new EventHandler(PgList_SelectedIndexChanged);
            pgList.KeyDown += new KeyEventHandler(PgList_KeyDown);
            pgList.MouseDoubleClick += new MouseEventHandler(PgList_DoubleClick);
            pgList.MouseDown += new MouseEventHandler(PgList_DragDown);
            pgList.MouseMove += new MouseEventHandler(PgList_DragMove);
            pgList.MouseUp += new MouseEventHandler(PgList_DragUp);
            pgList.QueryContinueDrag += new QueryContinueDragEventHandler(PgList_QueryDrag);
            pgList.GiveFeedback += new GiveFeedbackEventHandler(PgList_GiveFeedback);
        }

        ListBox MakeList()
        {
            ListBox lb = new ListBox();
            lb.Dock = DockStyle.Fill;
            lb.BorderStyle = BorderStyle.None;
            lb.DrawMode = DrawMode.OwnerDrawFixed;
            lb.ItemHeight = Px(32);
            lb.IntegralHeight = false;
            lb.Font = new Font(FONT_NAME, 10f);
            lb.DrawItem += new DrawItemEventHandler(List_DrawItem);
            lb.MouseDown += new MouseEventHandler(List_MouseDown);
            return lb;
        }

        Panel MakeSidePanel(string title, ListBox list, string btnText, EventHandler onAdd)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;

            Label head = new Label();
            head.Text = "  " + title;
            head.Dock = DockStyle.Top;
            head.Height = Px(32);
            head.TextAlign = ContentAlignment.MiddleLeft;
            head.Font = new Font(FONT_NAME, 9f, FontStyle.Bold);

            Button add = new Button();
            add.Text = btnText;
            add.Dock = DockStyle.Bottom;
            add.Height = Px(36);
            add.FlatStyle = FlatStyle.Flat;
            add.FlatAppearance.BorderSize = 0;
            add.TextAlign = ContentAlignment.MiddleLeft;
            add.Padding = new Padding(Px(8), 0, 0, 0);
            add.Cursor = Cursors.Hand;
            add.TabStop = false;
            add.Click += onAdd;

            p.Controls.Add(list);
            p.Controls.Add(head);
            p.Controls.Add(add);
            sidePanels.Add(p);
            sideHeads.Add(head);
            sideAdds.Add(add);
            return p;
        }

        void BuildToolbar()
        {
            tool = new ToolStrip();
            tool.GripStyle = ToolStripGripStyle.Hidden;
            tool.ImageScalingSize = new Size(Px(16), Px(16));
            tool.Padding = new Padding(Px(6), Px(3), Px(6), Px(3));
            tool.Font = new Font(FONT_NAME, 9f);

            AddBtn("+ 노트북", "새 노트북 (Ctrl+Shift+N)", new EventHandler(NewNotebook_Click));
            AddBtn("+ 페이지", "새 페이지 (Ctrl+N)", new EventHandler(NewPage_Click));
            tool.Items.Add(new ToolStripSeparator());

            boldBtn = AddBtn("B", "굵게 (Ctrl+B)", new EventHandler(Bold_Click));
            boldBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            italicBtn = AddBtn("I", "기울임 (Ctrl+I)", new EventHandler(Italic_Click));
            italicBtn.Font = new Font("Times New Roman", 11f, FontStyle.Italic | FontStyle.Bold);
            underBtn = AddBtn("U", "밑줄 (Ctrl+U)", new EventHandler(Under_Click));
            underBtn.Font = new Font("Segoe UI", 10f, FontStyle.Underline);
            strikeBtn = AddBtn("S", "취소선 (Ctrl+-)", new EventHandler(Strike_Click));
            strikeBtn.Font = new Font("Segoe UI", 10f, FontStyle.Strikeout);
            tool.Items.Add(new ToolStripSeparator());

            sizeBox = new ToolStripComboBox();
            sizeBox.AutoSize = false;
            sizeBox.Width = Px(52);
            sizeBox.FlatStyle = FlatStyle.Flat;
            sizeBox.ToolTipText = "글자 크기 (Ctrl+Shift+> / <)";
            string[] sizes = { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72" };
            sizeBox.Items.AddRange(sizes);
            sizeBox.SelectedIndexChanged += new EventHandler(SizeBox_Changed);
            sizeBox.KeyDown += new KeyEventHandler(SizeBox_KeyDown);
            tool.Items.Add(sizeBox);

            headBtn = new ToolStripDropDownButton("제목");
            headBtn.ToolTipText = "제목 스타일";
            AddMenu(headBtn, "제목 1  (Ctrl+Alt+1)", 1, new EventHandler(Heading_Click));
            AddMenu(headBtn, "제목 2  (Ctrl+Alt+2)", 2, new EventHandler(Heading_Click));
            AddMenu(headBtn, "제목 3  (Ctrl+Alt+3)", 3, new EventHandler(Heading_Click));
            AddMenu(headBtn, "본문  (Ctrl+Alt+0)", 0, new EventHandler(Heading_Click));
            tool.Items.Add(headBtn);
            tool.Items.Add(new ToolStripSeparator());

            foreBtn = new ToolStripSplitButton("글자색");
            foreBtn.Image = MakeSwatch(NoteEditor.RecentFore);
            foreBtn.ToolTipText = "글자색 (Ctrl+Shift+C: 최근 색)";
            foreBtn.ButtonClick += new EventHandler(ForeBtn_Click);
            AddColorItems(foreBtn, NoteEditor.FORE_COLORS, NoteEditor.FORE_NAMES, new EventHandler(ForePick_Click), true);
            tool.Items.Add(foreBtn);

            hiBtn = new ToolStripSplitButton("형광펜");
            hiBtn.Image = MakeSwatch(NoteEditor.RecentHi);
            hiBtn.ToolTipText = "형광펜 (Ctrl+Shift+H: 최근 색)";
            hiBtn.ButtonClick += new EventHandler(HiBtn_Click);
            AddColorItems(hiBtn, NoteEditor.HI_COLORS, NoteEditor.HI_NAMES, new EventHandler(HiPick_Click), true);
            NoteEditor.RecentChanged += new EventHandler(Recent_Changed);
            ToolStripMenuItem none = new ToolStripMenuItem("형광펜 없음");
            none.Tag = Color.Empty;
            none.Click += new EventHandler(HiPick_Click);
            hiBtn.DropDownItems.Add(none);
            tool.Items.Add(hiBtn);

            Color[] bc = { Color.White, Color.FromArgb(255, 251, 234), Color.FromArgb(255, 249, 196), Color.FromArgb(232, 245, 233),
                           Color.FromArgb(227, 242, 253), Color.FromArgb(243, 229, 245), Color.FromArgb(252, 228, 236), Color.FromArgb(245, 245, 245) };
            string[] bn = { "흰색", "아이보리", "연노랑", "연두", "하늘", "연보라", "연분홍", "연회색" };
            bgBtn = new ToolStripDropDownButton("배경");
            bgBtn.Image = MakeSwatch(Color.White);
            bgBtn.ToolTipText = "페이지 배경색";
            AddColorItems(bgBtn, bc, bn, new EventHandler(BgPick_Click), true);
            tool.Items.Add(bgBtn);
            tool.Items.Add(new ToolStripSeparator());

            AddBtn("□ 할일", "할 일 체크박스 (Ctrl+1)  /  [] 입력", new EventHandler(Todo_Click));
            AddBtn("• 목록", "글머리 기호 (Ctrl+.)  /  줄 처음에 - 입력 후 스페이스", new EventHandler(Bullet_Click));
            snipBtn = new ToolStripDropDownButton("상용구");
            snipBtn.ToolTipText = "자주 쓰는 문구 넣기 (Alt+1~9)";
            tool.Items.Add(snipBtn);
            tool.Items.Add(new ToolStripSeparator());
            AddBtn("찾기", "찾기 (Ctrl+F) / 바꾸기 (Ctrl+H)", new EventHandler(Find_Click));

            darkBtn = new ToolStripButton("\u25D0 다크 모드");
            darkBtn.ToolTipText = "다크 모드 켜기/끄기 (Ctrl+Shift+D)";
            darkBtn.Alignment = ToolStripItemAlignment.Right;
            darkBtn.Click += new EventHandler(Dark_Click);
            tool.Items.Add(darkBtn);

            topBtn = new ToolStripButton("항상 위");
            topBtn.ToolTipText = "항상 위에 표시 (Ctrl+Shift+T)";
            topBtn.Alignment = ToolStripItemAlignment.Right;
            topBtn.Click += new EventHandler(Top_Click);
            tool.Items.Add(topBtn);
        }

        ToolStripButton AddBtn(string text, string tip, EventHandler h)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.ToolTipText = tip;
            b.Click += h;
            tool.Items.Add(b);
            return b;
        }

        void AddMenu(ToolStripDropDownItem parent, string text, object tag, EventHandler h)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.Tag = tag;
            mi.Click += h;
            parent.DropDownItems.Add(mi);
        }

        void AddColorItems(ToolStripDropDownItem parent, Color[] colors, string[] names, EventHandler h, bool addCustom)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem(names[i], MakeSwatch(colors[i]));
                mi.Tag = colors[i];
                mi.Click += h;
                parent.DropDownItems.Add(mi);
            }
            if (addCustom)
            {
                ToolStripMenuItem more = new ToolStripMenuItem("다른 색...");
                more.Tag = null;
                more.Click += h;
                parent.DropDownItems.Add(more);
            }
        }

        Bitmap MakeSwatch(Color c)
        {
            int n = Px(16);
            Bitmap b = new Bitmap(n, n);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                using (SolidBrush br = new SolidBrush(c)) g.FillRectangle(br, 1, 1, n - 2, n - 2);
                g.DrawRectangle(Pens.Gray, 1, 1, n - 3, n - 3);
            }
            return b;
        }

        Icon MakeIcon()
        {
            try { return Icon.FromHandle(AppIcon.DrawA(32).GetHicon()); }
            catch { return null; }
        }

        void BuildStatus()
        {
            status = new StatusStrip();
            status.SizingGrip = false;
            status.Font = new Font(FONT_NAME, 9f);
            stLeft = new ToolStripStatusLabel();
            stLeft.Spring = true;
            stLeft.TextAlign = ContentAlignment.MiddleLeft;
            stRight = new ToolStripStatusLabel();
            status.Items.Add(stLeft);
            status.Items.Add(stRight);
        }

        void BuildMenus()
        {
            nbMenu = new ContextMenuStrip();
            nbMenu.Items.Add("새 노트북", null, new EventHandler(NewNotebook_Click));
            nbMenu.Items.Add("이름 바꾸기 (F2)", null, new EventHandler(RenameNotebook_Click));
            nbMenu.Items.Add("위로 이동 (Alt+↑)", null, new EventHandler(NbUp_Click));
            nbMenu.Items.Add("아래로 이동 (Alt+↓)", null, new EventHandler(NbDown_Click));
            ToolStripMenuItem colorMenu = new ToolStripMenuItem("색 바꾸기");
            Color[] nc = { Color.FromArgb(0, 103, 192), Color.FromArgb(0, 153, 214), Color.FromArgb(0, 137, 123), Color.FromArgb(56, 142, 60),
                           Color.FromArgb(230, 170, 0), Color.FromArgb(217, 115, 13), Color.FromArgb(224, 62, 62), Color.FromArgb(194, 24, 91),
                           Color.FromArgb(105, 64, 165), Color.FromArgb(120, 120, 120) };
            string[] nn = { "파랑", "하늘", "청록", "초록", "노랑", "주황", "빨강", "분홍", "보라", "회색" };
            AddColorItems(colorMenu, nc, nn, new EventHandler(NbColor_Click), true);
            ToolStripMenuItem autoColor = new ToolStripMenuItem("자동 색");
            autoColor.Tag = Color.Empty;
            autoColor.Click += new EventHandler(NbColor_Click);
            colorMenu.DropDownItems.Add(autoColor);
            nbMenu.Items.Add(colorMenu);
            nbMenu.Items.Add(new ToolStripSeparator());
            nbMenu.Items.Add("삭제 (Del)", null, new EventHandler(DeleteNotebook_Click));
            nbList.ContextMenuStrip = nbMenu;

            pgMenu = new ContextMenuStrip();
            pgMenu.Items.Add("새 페이지 (Ctrl+N)", null, new EventHandler(NewPage_Click));
            pgMenu.Items.Add("이름 바꾸기 (F2)", null, new EventHandler(RenamePage_Click));
            pgMenu.Items.Add("위로 이동 (Alt+↑)", null, new EventHandler(PgUp_Click));
            pgMenu.Items.Add("아래로 이동 (Alt+↓)", null, new EventHandler(PgDown_Click));
            moveToMenu = new ToolStripMenuItem("다른 노트북으로 이동");
            pgMenu.Items.Add(moveToMenu);
            pgMenu.Items.Add(new ToolStripSeparator());
            pgMenu.Items.Add("삭제 (Del)", null, new EventHandler(DeletePage_Click));
            pgMenu.Items.Insert(0, new ToolStripSeparator());
            favMenuItem = new ToolStripMenuItem("즐겨찾기 추가 (Ctrl+D)", null, new EventHandler(FavToggle_Click));
            pgMenu.Items.Insert(0, favMenuItem);
            stickyMenuItem = new ToolStripMenuItem("포스트잇으로 띄우기", null, new EventHandler(StickyOpen_Click));
            pgMenu.Items.Insert(0, stickyMenuItem);
            pgMenu.Items.Insert(2, new ToolStripMenuItem("QR로 보기 (Ctrl+Shift+Q)", null, new EventHandler(Qr_Click)));
            pgMenu.Opening += new System.ComponentModel.CancelEventHandler(PgMenu_Opening);
            pgList.ContextMenuStrip = pgMenu;

            edMenu = new ContextMenuStrip();
            edMenu.Items.Add("잘라내기 (Ctrl+X)", null, new EventHandler(Cut_Click));
            edMenu.Items.Add("복사 (Ctrl+C)", null, new EventHandler(Copy_Click));
            edMenu.Items.Add("붙여넣기 (Ctrl+V)", null, new EventHandler(Paste_Click));
            edMenu.Items.Add("텍스트만 붙여넣기 (Ctrl+Shift+V)", null, new EventHandler(PastePlain_Click));
            edMenu.Items.Add(new ToolStripSeparator());
            edMenu.Items.Add("모두 선택 (Ctrl+A)", null, new EventHandler(SelectAll_Click));
            ed.AddColorMenus(edMenu);
            SnippetMenu sm = new SnippetMenu(ed, baseFont, S);
            sm.Attach(edMenu);
            sm.AttachButton(snipBtn);
            ed.ContextMenuStrip = edMenu;
        }

        // ================= 시작 / 종료 =================

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try { split1.SplitterDistance = Px(190); } catch { }
            try { split2.SplitterDistance = Px(230); } catch { }

            LoadNotebooks();
            bool first = nbOrder.Count == 0;
            if (first)
            {
                Directory.CreateDirectory(NbDir("내 노트북"));
                CreatePageFile("내 노트북", "AHNNOTE 사용법");
                LoadNotebooks();
            }

            string stNb = null, stPg = null;
            try
            {
                string sf = Path.Combine(dataDir, "_state.txt");
                if (File.Exists(sf))
                {
                    string[] st = File.ReadAllLines(sf, Encoding.UTF8);
                    if (st.Length > 0) stNb = st[0];
                    if (st.Length > 1) stPg = st[1];
                }
            }
            catch { }

            int i = FillList(nbList, nbOrder, stNb);
            OpenNotebook(nbOrder[i], stPg);
            if (first)
            {
                WriteWelcome();
                dirty = true;
                SaveCurrent();
            }
            ed.Focus();

            UpdateCalButton();
            alarmTimer = new Timer();
            alarmTimer.Interval = 15000;
            alarmTimer.Tick += new EventHandler(AlarmTimer_Tick);
            alarmTimer.Start();
            LoadFavorites();
            RestoreStickies();
            CheckAlarms(true);
        }

        bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string t = Path.Combine(dir, "_write_test.tmp");
                File.WriteAllText(t, "ok");
                File.Delete(t);
                return true;
            }
            catch { return false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CommitTitle();
            SaveCurrent();
            SaveState();
            appClosing = true;
            foreach (StickyNote sn in stickies) { sn.AppClosing = true; sn.SaveNow(); }
            SaveStickies();
            base.OnFormClosing(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            SaveCurrent();
        }

        void SaveTimer_Tick(object sender, EventArgs e)
        {
            if (dirty) SaveCurrent();
            UpdateStatus();
        }

        // ================= 단축키 =================

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S: dirty = true; SaveCurrent(); UpdateStatus(); return true;
                case Keys.Control | Keys.N: NewPage(); return true;
                case Keys.Control | Keys.Shift | Keys.N: NewNotebook(); return true;
                case Keys.Control | Keys.F: ShowFind(false); return true;
                case Keys.Control | Keys.H: ShowFind(true); return true;
                case Keys.F3: FindAgain(true); return true;
                case Keys.Control | Keys.Shift | Keys.D: ToggleDark(); return true;
                case Keys.Control | Keys.Shift | Keys.T: ToggleTop(); return true;
                case Keys.Control | Keys.D: ToggleFavorite(); return true;
                case Keys.Control | Keys.Shift | Keys.Q: ShowQr(); return true;
                case Keys.Shift | Keys.F3: FindAgain(false); return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================= 저장 파일 =================

        string NbDir(string nb) { return Path.Combine(dataDir, nb); }
        string PagePath(string nb, string pg) { return Path.Combine(NbDir(nb), pg + ".rtf"); }
        string NbMetaFile() { return Path.Combine(dataDir, "_notebooks.txt"); }
        string PgMetaFile(string nb) { return Path.Combine(NbDir(nb), "_pages.txt"); }

        List<string> ReadMeta(string file, Dictionary<string, string> vals)
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(file)) return list;
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    if (line.Length == 0) continue;
                    string name = line;
                    int tab = line.IndexOf('\t');
                    if (tab >= 0)
                    {
                        name = line.Substring(0, tab);
                        if (vals != null) vals[name] = line.Substring(tab + 1);
                    }
                    list.Add(name);
                }
            }
            catch { }
            return list;
        }

        void WriteMeta(string file, List<string> list, Dictionary<string, string> vals)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string n in list)
                {
                    sb.Append(n);
                    string v;
                    if (vals != null && vals.TryGetValue(n, out v)) sb.Append('\t').Append(v);
                    sb.Append("\r\n");
                }
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        static int IndexOfName(List<string> list, string name)
        {
            if (name == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static List<string> MergeOrder(List<string> meta, List<string> actual)
        {
            List<string> result = new List<string>();
            foreach (string m in meta)
            {
                int i = IndexOfName(actual, m);
                if (i >= 0 && IndexOfName(result, actual[i]) < 0) result.Add(actual[i]);
            }
            List<string> rest = new List<string>();
            foreach (string a in actual)
                if (IndexOfName(result, a) < 0) rest.Add(a);
            rest.Sort(StringComparer.CurrentCultureIgnoreCase);
            result.AddRange(rest);
            return result;
        }

        static string UniqueName(List<string> list, string baseName)
        {
            if (IndexOfName(list, baseName) < 0) return baseName;
            int n = 2;
            while (IndexOfName(list, baseName + " (" + n + ")") >= 0) n++;
            return baseName + " (" + n + ")";
        }

        static string CleanName(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in s)
                sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim().TrimEnd('.');
            if (r.Length > 80) r = r.Substring(0, 80).Trim();
            if (r == TRASH || r.StartsWith("_")) r = "-" + r.TrimStart('_');
            return r;
        }

        List<string> PageFiles(string nb)
        {
            List<string> list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(NbDir(nb), "*.rtf"))
                    if (string.Equals(Path.GetExtension(f), ".rtf", StringComparison.OrdinalIgnoreCase))
                        list.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            return list;
        }

        void LoadNotebooks()
        {
            List<string> actual = new List<string>();
            foreach (string d in Directory.GetDirectories(dataDir))
            {
                string n = Path.GetFileName(d);
                if (n == TRASH || n.StartsWith(".") || n.StartsWith("_")) continue;
                actual.Add(n);
            }
            nbColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            nbOrder = MergeOrder(ReadMeta(NbMetaFile(), nbColors), actual);
        }

        void LoadPages(string nb)
        {
            pgBg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> meta = ReadMeta(PgMetaFile(nb), pgBg);
            pgOrder = MergeOrder(meta, PageFiles(nb));
        }

        void SaveNbMeta() { WriteMeta(NbMetaFile(), nbOrder, nbColors); }
        void SavePgMeta() { if (curNotebook != null) WriteMeta(PgMetaFile(curNotebook), pgOrder, pgBg); }

        void SaveState()
        {
            try
            {
                File.WriteAllText(Path.Combine(dataDir, "_state.txt"),
                    (curNotebook == null ? "" : curNotebook) + "\r\n" + (curPage == null ? "" : curPage), Encoding.UTF8);
            }
            catch { }
        }

        void CreatePageFile(string nb, string pg)
        {
            File.WriteAllText(PagePath(nb, pg), "{\\rtf1\\ansi\\deff0 }", Encoding.ASCII);
        }

        string TrashPath(string name)
        {
            string t = Path.Combine(dataDir, TRASH);
            Directory.CreateDirectory(t);
            return Path.Combine(t, name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }

        static void MoveDir(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                string tmp = a + "_ren_tmp";
                Directory.Move(a, tmp);
                Directory.Move(tmp, b);
            }
            else Directory.Move(a, b);
        }

        static void MoveFile(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                string tmp = a + ".ren_tmp";
                File.Move(a, tmp);
                File.Move(tmp, b);
            }
            else File.Move(a, b);
        }

        // ================= 목록 =================

        int FillList(ListBox lb, List<string> items, string select)
        {
            listBusy = true;
            lb.BeginUpdate();
            lb.Items.Clear();
            foreach (string s in items) lb.Items.Add(s);
            lb.EndUpdate();
            int i = IndexOfName(items, select);
            if (i < 0 && items.Count > 0) i = 0;
            lb.SelectedIndex = i;
            listBusy = false;
            return i;
        }

        void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            ListBox lb = (ListBox)sender;
            if (e.Index < 0 || e.Index >= lb.Items.Count) return;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush b = new SolidBrush(sel ? cSel : lb.BackColor))
                e.Graphics.FillRectangle(b, e.Bounds);
            int x = e.Bounds.X + Px(12);
            if (lb == nbList)
            {
                int d = Px(10);
                Color c = NbColor(e.Index);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(c))
                    e.Graphics.FillRectangle(b, x, e.Bounds.Y + (e.Bounds.Height - d) / 2, d, d);
                x += Px(20);
            }
            if (sel)
            {
                using (SolidBrush b = new SolidBrush(cAccent))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, Px(4), e.Bounds.Height);
            }
            string name = lb.Items[e.Index].ToString();
            int right = e.Bounds.Right - Px(4);
            if (lb == pgList && curNotebook != null)
            {
                if (IsFav(curNotebook, name))
                {
                    Rectangle sr = new Rectangle(x - Px(2), e.Bounds.Y, Px(16), e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, "★", lb.Font, sr, Color.FromArgb(230, 170, 0),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += Px(16);
                }
                if (FindSticky(curNotebook, name) != null)
                {
                    Rectangle pr = new Rectangle(right - Px(52), e.Bounds.Y, Px(52), e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, "포스트잇", favSmallFont, pr, cMuted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    right -= Px(56);
                }
            }
            Rectangle r = new Rectangle(x, e.Bounds.Y, right - x, e.Bounds.Height);
            Font f = sel ? new Font(lb.Font, FontStyle.Bold) : lb.Font;
            TextRenderer.DrawText(e.Graphics, name, f, r, cText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (sel) f.Dispose();
        }

        void List_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListBox lb = (ListBox)sender;
            int i = lb.IndexFromPoint(e.Location);
            if (i >= 0 && i != lb.SelectedIndex) lb.SelectedIndex = i;
        }

        void NbList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBusy || nbList.SelectedIndex < 0) return;
            string nb = nbOrder[nbList.SelectedIndex];
            if (nb == curNotebook) return;
            OpenNotebook(nb, null);
        }

        void PgList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBusy || pgList.SelectedIndex < 0) return;
            string pg = pgOrder[pgList.SelectedIndex];
            if (pg == curPage) return;
            OpenPage(pg);
        }

        void NbList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2) { RenameNotebook(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteNotebook(); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Up) { MoveNotebook(-1); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Down) { MoveNotebook(1); e.Handled = true; }
        }

        void PgList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2) { RenamePage(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeletePage(); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Up) { MovePage(-1); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Down) { MovePage(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { ed.Focus(); e.Handled = true; }
        }

        // ================= 노트북 =================

        void OpenNotebook(string nb, string page)
        {
            CommitTitle();
            SaveCurrent();
            curNotebook = nb;
            curPage = null;
            Directory.CreateDirectory(NbDir(nb));
            LoadPages(nb);
            if (pgOrder.Count == 0)
            {
                CreatePageFile(nb, "새 페이지");
                LoadPages(nb);
            }
            int i = FillList(pgList, pgOrder, page);
            OpenPage(pgOrder[i]);
        }

        void NewNotebook()
        {
            string name = InputDialog.Ask(this, "새 노트북", "노트북 이름:", UniqueName(nbOrder, "새 노트북"));
            if (name == null) return;
            name = CleanName(name);
            if (name.Length == 0) return;
            if (IndexOfName(nbOrder, name) >= 0) { MessageBox.Show(this, "같은 이름의 노트북이 이미 있습니다.", "AHNNOTE"); return; }
            try { Directory.CreateDirectory(NbDir(name)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AHNNOTE"); return; }
            nbOrder.Add(name);
            SaveNbMeta();
            FillList(nbList, nbOrder, name);
            OpenNotebook(name, null);
            titleBox.Focus();
            titleBox.SelectAll();
        }

        void RenameNotebook()
        {
            int idx = nbList.SelectedIndex;
            if (idx < 0) return;
            string old = nbOrder[idx];
            string name = InputDialog.Ask(this, "노트북 이름 바꾸기", "새 이름:", old);
            if (name == null) return;
            name = CleanName(name);
            if (name.Length == 0 || name == old) return;
            int other = IndexOfName(nbOrder, name);
            if (other >= 0 && other != idx) { MessageBox.Show(this, "같은 이름의 노트북이 이미 있습니다.", "AHNNOTE"); return; }
            if (StickyBlocks(old, null)) return;
            CommitTitle();
            SaveCurrent();
            try { MoveDir(NbDir(old), NbDir(name)); }
            catch (Exception ex) { MessageBox.Show(this, "이름을 바꿀 수 없습니다.\n" + ex.Message, "AHNNOTE"); return; }
            nbOrder[idx] = name;
            string col;
            if (nbColors.TryGetValue(old, out col)) { nbColors.Remove(old); nbColors[name] = col; }
            RenameFavNotebook(old, name);
            SaveNbMeta();
            if (curNotebook == old) curNotebook = name;
            FillList(nbList, nbOrder, name);
            SaveState();
            UpdateStatus();
        }

        void DeleteNotebook()
        {
            int idx = nbList.SelectedIndex;
            if (idx < 0) return;
            string nb = nbOrder[idx];
            if (StickyBlocks(nb, null)) return;
            if (MessageBox.Show(this, "'" + nb + "' 노트북을 삭제할까요?\n(" + TRASH + " 폴더로 옮겨집니다)", "AHNNOTE",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SaveCurrent();
            try { Directory.Move(NbDir(nb), TrashPath(nb)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AHNNOTE"); return; }
            curNotebook = null;
            curPage = null;
            nbOrder.RemoveAt(idx);
            nbColors.Remove(nb);
            RenameFavNotebook(nb, null);
            if (nbOrder.Count == 0)
            {
                Directory.CreateDirectory(NbDir("내 노트북"));
                nbOrder.Add("내 노트북");
            }
            SaveNbMeta();
            int i = Math.Min(idx, nbOrder.Count - 1);
            FillList(nbList, nbOrder, nbOrder[i]);
            OpenNotebook(nbOrder[i], null);
        }

        void MoveNotebook(int delta)
        {
            int i = nbList.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= nbOrder.Count) return;
            string t = nbOrder[i]; nbOrder[i] = nbOrder[j]; nbOrder[j] = t;
            SaveNbMeta();
            FillList(nbList, nbOrder, t);
        }

        // ================= 페이지 =================

        void OpenPage(string pg)
        {
            CommitTitle();
            SaveCurrent();
            curPage = pg;
            loading = true;
            string path = PagePath(curNotebook, pg);
            ed.LoadPage(path);

            Color bg = Color.White;
            string v;
            if (pgBg.TryGetValue(pg, out v))
            {
                int argb;
                if (int.TryParse(v, out argb)) bg = Color.FromArgb(argb);
            }
            ApplyBgColor(bg);

            titleBox.Text = pg;
            UpdateDateLabel();

            ed.Modified = false;
            dirty = false;
            loading = false;
            ApplyLock();
            SaveState();
            UpdateStatus();
            UpdateToolbar();
        }

        void SaveCurrent()
        {
            if (curNotebook == null || curPage == null) return;
            if (!dirty && !ed.Modified) return;
            if (FindSticky(curNotebook, curPage) != null) { dirty = false; ed.Modified = false; return; }   // 포스트잇에서 편집 중
            string path = PagePath(curNotebook, curPage);
            string err = ed.SavePage(path);
            if (err == null)
            {
                lastSaved = DateTime.Now.ToString("HH:mm:ss");
                UpdateDateLabel();
            }
            else stLeft.Text = "저장 실패: " + err;
        }

        void NewPage()
        {
            if (curNotebook == null) return;
            CommitTitle();
            SaveCurrent();
            string name = UniqueName(pgOrder, "새 페이지");
            try { CreatePageFile(curNotebook, name); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AHNNOTE"); return; }
            int at = IndexOfName(pgOrder, curPage) + 1;
            if (at <= 0) at = pgOrder.Count;
            pgOrder.Insert(at, name);
            SavePgMeta();
            FillList(pgList, pgOrder, name);
            OpenPage(name);
            titleBox.Focus();
            titleBox.SelectAll();
        }

        void RenamePage()
        {
            titleBox.Focus();
            titleBox.SelectAll();
        }

        void CommitTitle()
        {
            if (curNotebook == null || curPage == null) return;
            if (titleBox.Text == curPage) return;
            if (FindSticky(curNotebook, curPage) != null) { titleBox.Text = curPage; return; }
            string nn = CleanName(titleBox.Text);
            if (nn.Length == 0 || nn == curPage) { titleBox.Text = curPage; return; }
            int me = IndexOfName(pgOrder, curPage);
            int other = IndexOfName(pgOrder, nn);
            if (other >= 0 && other != me)
            {
                MessageBox.Show(this, "같은 이름의 페이지가 이미 있습니다.", "AHNNOTE");
                titleBox.Text = curPage;
                return;
            }
            SaveCurrent();
            try { MoveFile(PagePath(curNotebook, curPage), PagePath(curNotebook, nn)); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "이름을 바꿀 수 없습니다.\n" + ex.Message, "AHNNOTE");
                titleBox.Text = curPage;
                return;
            }
            string bg;
            if (pgBg.TryGetValue(curPage, out bg)) { pgBg.Remove(curPage); pgBg[nn] = bg; }
            if (me >= 0) pgOrder[me] = nn;
            RenameFav(curNotebook, curPage, curNotebook, nn);
            curPage = nn;
            SavePgMeta();
            titleBox.Text = nn;
            if (me >= 0) FillList(pgList, pgOrder, nn);
            SaveState();
            UpdateStatus();
        }

        void TitleBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                ed.Focus();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                titleBox.Text = curPage;
                ed.Focus();
            }
        }

        void TitleBox_Leave(object sender, EventArgs e) { CommitTitle(); }

        void DeletePage()
        {
            if (curPage == null) return;
            if (StickyBlocks(curNotebook, curPage)) return;
            if (MessageBox.Show(this, "'" + curPage + "' 페이지를 삭제할까요?\n(" + TRASH + " 폴더로 옮겨집니다)", "AHNNOTE",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            titleBox.Text = curPage;
            SaveCurrent();
            int idx = IndexOfName(pgOrder, curPage);
            try { File.Move(PagePath(curNotebook, curPage), TrashPath(curNotebook + "_" + curPage) + ".rtf"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AHNNOTE"); return; }
            pgBg.Remove(curPage);
            RemoveFav(curNotebook, curPage);
            if (idx >= 0) pgOrder.RemoveAt(idx);
            curPage = null;
            if (pgOrder.Count == 0)
            {
                CreatePageFile(curNotebook, "새 페이지");
                pgOrder.Add("새 페이지");
            }
            SavePgMeta();
            int i = Math.Max(0, Math.Min(idx, pgOrder.Count - 1));
            FillList(pgList, pgOrder, pgOrder[i]);
            OpenPage(pgOrder[i]);
        }

        void MovePage(int delta)
        {
            int i = IndexOfName(pgOrder, curPage);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= pgOrder.Count) return;
            string t = pgOrder[i]; pgOrder[i] = pgOrder[j]; pgOrder[j] = t;
            SavePgMeta();
            FillList(pgList, pgOrder, curPage);
        }

        void MovePageTo(string target)
        {
            if (curPage == null || target == curNotebook) return;
            if (StickyBlocks(curNotebook, curPage)) return;
            CommitTitle();
            SaveCurrent();
            Dictionary<string, string> tv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> tactual = PageFiles(target);
            List<string> tmeta = MergeOrder(ReadMeta(PgMetaFile(target), tv), tactual);
            string nn = UniqueName(tactual, curPage);
            try { File.Move(PagePath(curNotebook, curPage), PagePath(target, nn)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AHNNOTE"); return; }
            tmeta.Add(nn);
            string bg;
            if (pgBg.TryGetValue(curPage, out bg)) { tv[nn] = bg; pgBg.Remove(curPage); }
            WriteMeta(PgMetaFile(target), tmeta, tv);
            RenameFav(curNotebook, curPage, target, nn);

            int idx = IndexOfName(pgOrder, curPage);
            if (idx >= 0) pgOrder.RemoveAt(idx);
            curPage = null;
            SavePgMeta();
            string sel = null;
            if (pgOrder.Count > 0) sel = pgOrder[Math.Max(0, Math.Min(idx, pgOrder.Count - 1))];
            OpenNotebook(curNotebook, sel);
            stLeft.Text = "'" + nn + "' 페이지를 '" + target + "' 노트북으로 옮겼습니다.";
        }

        void PgMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            moveToMenu.DropDownItems.Clear();
            foreach (string nb in nbOrder)
            {
                if (nb == curNotebook) continue;
                ToolStripMenuItem mi = new ToolStripMenuItem(nb);
                mi.Tag = nb;
                mi.Click += new EventHandler(MoveTo_Click);
                moveToMenu.DropDownItems.Add(mi);
            }
            moveToMenu.Enabled = moveToMenu.DropDownItems.Count > 0;
            bool fav = curPage != null && IsFav(curNotebook, curPage);
            favMenuItem.Text = fav ? "즐겨찾기에서 빼기 (Ctrl+D)" : "즐겨찾기 추가 (Ctrl+D)";
            bool st = curPage != null && FindSticky(curNotebook, curPage) != null;
            stickyMenuItem.Text = st ? "포스트잇 보기" : "포스트잇으로 띄우기";
        }

        void ApplyBgColor(Color light)
        {
            Color c = ColorMap.Disp(light);
            ed.BackColor = c;
            pagePanel.BackColor = c;
            titleBox.BackColor = c;
            dateLabel.BackColor = c;
            bgBtn.Image = MakeSwatch(light);
        }

        void SetPageBg(Color c)
        {
            if (curPage == null) return;
            ApplyBgColor(c);
            if (c.ToArgb() == Color.White.ToArgb()) pgBg.Remove(curPage);
            else pgBg[curPage] = c.ToArgb().ToString();
            SavePgMeta();
        }

        // ================= 테마 / 다크 모드 =================

        void LoadSettings()
        {
            try
            {
                string f = Path.Combine(dataDir, "_settings.txt");
                if (!File.Exists(f)) return;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "dark") dark = (v == "1");
                    else if (k == "top") topMost = (v == "1");
                    else if (k == "molebest") int.TryParse(v, out moleBest);
                    else if (k == "qrsize") int.TryParse(v, out QrForm.LastSize);
                    else if (k == "ioslink" && v.StartsWith("https://")) QrForm.IosLink = v;
                    else if (k == "fore" || k == "hi")
                    {
                        int argb;
                        if (int.TryParse(v, out argb))
                        {
                            if (k == "fore") NoteEditor.RecentFore = Color.FromArgb(argb);
                            else NoteEditor.RecentHi = Color.FromArgb(argb);
                        }
                    }
                }
            }
            catch { }
        }

        void SaveSettings()
        {
            string t = "dark=" + (dark ? "1" : "0") + "\r\n" + "top=" + (topMost ? "1" : "0") + "\r\n" + "molebest=" + moleBest + "\r\n"
                + "fore=" + NoteEditor.RecentFore.ToArgb() + "\r\n" + "hi=" + NoteEditor.RecentHi.ToArgb() + "\r\n"
                + "qrsize=" + QrForm.LastSize + "\r\n" + "ioslink=" + QrForm.IosLink + "\r\n";
            try { File.WriteAllText(Path.Combine(dataDir, "_settings.txt"), t, Encoding.UTF8); }
            catch { }
        }

        void SetPalette()
        {
            if (!dark)
            {
                cAccent = Color.FromArgb(0, 103, 192);
                cLogo = Color.FromArgb(0, 103, 192);
                cWin = Color.White;
                cSide = Color.FromArgb(243, 247, 252);
                cList = Color.White;
                cSel = Color.FromArgb(217, 233, 250);
                cText = Color.FromArgb(32, 32, 32);
                cMuted = Color.FromArgb(110, 115, 122);
                cLine = Color.FromArgb(222, 226, 232);
                cBar = Color.White;
            }
            else
            {
                cAccent = Color.FromArgb(96, 180, 255);
                cLogo = Color.FromArgb(0, 84, 158);
                cWin = Color.FromArgb(32, 32, 32);
                cSide = Color.FromArgb(27, 27, 29);
                cList = Color.FromArgb(36, 36, 38);
                cSel = Color.FromArgb(38, 60, 88);
                cText = Color.FromArgb(232, 232, 232);
                cMuted = Color.FromArgb(150, 154, 160);
                cLine = Color.FromArgb(55, 55, 58);
                cBar = Color.FromArgb(43, 43, 45);
            }
        }

        void ApplyTheme()
        {
            ColorMap.Dark = dark;
            SetPalette();
            BackColor = cWin;
            split1.BackColor = cLine;
            split2.BackColor = cLine;
            split1.Panel1.BackColor = cSide;
            split1.Panel2.BackColor = cList;
            split2.Panel1.BackColor = cList;
            split2.Panel2.BackColor = cWin;
            nbList.BackColor = cSide;
            pgList.BackColor = cList;
            sidePanels[0].BackColor = cList;   // 페이지
            sidePanels[1].BackColor = cSide;   // 노트북
            foreach (Label h in sideHeads) h.ForeColor = cMuted;
            foreach (Button b in sideAdds)
            {
                b.ForeColor = cAccent;
                b.FlatAppearance.MouseOverBackColor = cSel;
                b.FlatAppearance.MouseDownBackColor = cSel;
            }
            logo.BackColor = cLogo;
            logo.ForeColor = Color.White;
            favPanel.BackColor = cSide;
            favList.BackColor = cSide;
            favHead.ForeColor = cMuted;
            lockBar.BackColor = dark ? Color.FromArgb(58, 52, 30) : Color.FromArgb(255, 246, 200);
            lockLabel.ForeColor = cText;
            Skin.Apply(lockBar);
            calBtn.BackColor = cSide;
            calBtn.ForeColor = cAccent;
            calBtn.FlatAppearance.MouseOverBackColor = cSel;
            calBtn.FlatAppearance.MouseDownBackColor = cSel;
            titleLine.BackColor = cLine;
            titleBox.ForeColor = cText;
            dateLabel.ForeColor = cMuted;

            ToolStripManager.Renderer = new AhnRenderer(new AhnColors(dark), cText);
            tool.BackColor = cBar;
            status.BackColor = cBar;
            stLeft.ForeColor = cText;
            stRight.ForeColor = cText;
            sizeBox.ComboBox.BackColor = dark ? cList : Color.White;
            sizeBox.ComboBox.ForeColor = cText;
            darkBtn.Checked = dark;
            topBtn.Checked = topMost;
            TopMost = topMost;
            darkBtn.Text = dark ? "\u25D1 라이트 모드" : "\u25D0 다크 모드";

            Skin.SetDark(dark);
            Skin.TitleBar(this, dark);
            Skin.ScrollBars(ed, dark);
            Skin.ScrollBars(nbList, dark);
            Skin.ScrollBars(pgList, dark);
            if (findForm != null) Skin.Apply(findForm);
            if (calForm != null && !calForm.IsDisposed) calForm.ApplyColors();
            nbList.Invalidate();
            pgList.Invalidate();
            Invalidate(true);
        }

        void ToggleDark()
        {
            CommitTitle();
            SaveCurrent();          // 지금 모드 기준으로 먼저 저장
            foreach (StickyNote sn in stickies) sn.SaveNow();
            dark = !dark;
            SaveSettings();
            ApplyTheme();
            foreach (StickyNote sn in stickies) sn.ReloadTheme();
            if (curPage != null) OpenPage(curPage);
            ed.Focus();
        }

        void Dark_Click(object sender, EventArgs e) { ToggleDark(); }

        // ================= 노트북 색 / 항상 위 / 날짜 / 이스터에그 =================

        Color NbColor(int index)
        {
            if (index >= 0 && index < nbOrder.Count)
            {
                string v;
                int argb;
                if (nbColors.TryGetValue(nbOrder[index], out v) && int.TryParse(v, out argb)) return Color.FromArgb(argb);
            }
            return NB_COLORS[Math.Max(0, index) % NB_COLORS.Length];
        }

        void NbColor_Click(object sender, EventArgs e)
        {
            int idx = nbList.SelectedIndex;
            if (idx < 0) return;
            string nb = nbOrder[idx];
            object tag = ((ToolStripItem)sender).Tag;
            if (tag is Color && ((Color)tag).IsEmpty) nbColors.Remove(nb);
            else if (tag is Color) nbColors[nb] = ((Color)tag).ToArgb().ToString();
            else
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.FullOpen = true;
                    cd.Color = NbColor(idx);
                    if (cd.ShowDialog(this) != DialogResult.OK) return;
                    nbColors[nb] = cd.Color.ToArgb().ToString();
                }
            }
            SaveNbMeta();
            nbList.Invalidate();
        }

        void ToggleTop()
        {
            topMost = !topMost;
            TopMost = topMost;
            topBtn.Checked = topMost;
            if (findForm != null) findForm.TopMost = topMost;
            if (mole != null && !mole.IsDisposed) mole.TopMost = topMost;
            if (calForm != null && !calForm.IsDisposed) calForm.TopMost = topMost;
            SaveSettings();
            stLeft.Text = topMost ? "항상 위에 표시: 켜짐" : "항상 위에 표시: 꺼짐";
        }

        void Top_Click(object sender, EventArgs e) { ToggleTop(); }

        void UpdateDateLabel()
        {
            if (curNotebook == null || curPage == null) { dateLabel.Text = ""; return; }
            try
            {
                string path = PagePath(curNotebook, curPage);
                DateTime c = File.GetCreationTime(path);
                DateTime m = File.GetLastWriteTime(path);
                dateLabel.Text = c.ToString("yyyy년 M월 d일 dddd  tt h:mm") + "\n(최종 수정: " + m.ToString("yyyy년 M월 d일  tt h:mm") + ")";
            }
            catch { dateLabel.Text = ""; }
        }

        void NbList_DoubleClick(object sender, MouseEventArgs e)
        {
            if (nbList.IndexFromPoint(e.Location) >= 0) RenameNotebook();
        }

        void PgList_DoubleClick(object sender, MouseEventArgs e)
        {
            if (pgList.IndexFromPoint(e.Location) < 0 || curPage == null) return;
            if (StickyBlocks(curNotebook, curPage)) return;
            string name = InputDialog.Ask(this, "페이지 이름 바꾸기", "새 이름:", curPage);
            if (name == null) return;
            titleBox.Text = name;
            CommitTitle();
        }

        // 로고를 3초 동안 누르고 있으면 두더지 게임
        void Logo_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            holdTimer.Stop();
            holdTimer.Start();
        }

        void Logo_MouseUp(object sender, MouseEventArgs e) { holdTimer.Stop(); }
        void Logo_MouseLeave(object sender, EventArgs e) { holdTimer.Stop(); }

        void Logo_MouseMove(object sender, MouseEventArgs e)
        {
            if (holdTimer.Enabled && !logo.ClientRectangle.Contains(e.Location)) holdTimer.Stop();
        }

        void HoldTimer_Tick(object sender, EventArgs e)
        {
            holdTimer.Stop();
            if (mole != null && !mole.IsDisposed) { mole.Activate(); return; }
            mole = new MoleGame(moleBest, S);
            mole.FormClosed += new FormClosedEventHandler(Mole_Closed);
            mole.StartPosition = FormStartPosition.Manual;
            mole.TopMost = TopMost;
            mole.Location = new Point(Left + (Width - mole.Width) / 2, Top + (Height - mole.Height) / 2);
            mole.Show(this);
        }

        void Mole_Closed(object sender, FormClosedEventArgs e)
        {
            if (mole != null && mole.Best > moleBest)
            {
                moleBest = mole.Best;
                SaveSettings();
            }
            mole = null;
        }

        // ================= 즐겨찾기 =================

        void BuildFavorites()
        {
            favPanel = new Panel();
            favPanel.Dock = DockStyle.Top;
            favPanel.Visible = false;
            favHead = new Label();
            favHead.Text = "  즐겨찾기";
            favHead.Dock = DockStyle.Top;
            favHead.Height = Px(30);
            favHead.TextAlign = ContentAlignment.MiddleLeft;
            favHead.Font = new Font(FONT_NAME, 9f, FontStyle.Bold);
            favList = MakeList();
            favList.ItemHeight = Px(28);
            favList.DrawItem -= new DrawItemEventHandler(List_DrawItem);
            favList.DrawItem += new DrawItemEventHandler(FavList_DrawItem);
            favList.MouseUp += new MouseEventHandler(FavList_MouseUp);
            favMenu = new ContextMenuStrip();
            favMenu.Items.Add("포스트잇으로 띄우기", null, new EventHandler(FavSticky_Click));
            favMenu.Items.Add("즐겨찾기에서 빼기", null, new EventHandler(FavRemove_Click));
            favList.ContextMenuStrip = favMenu;
            favPanel.Controls.Add(favList);
            favPanel.Controls.Add(favHead);
        }

        string FavKey(string nb, string pg) { return nb + "\t" + pg; }

        bool IsFav(string nb, string pg)
        {
            return favs.IndexOf(FavKey(nb, pg)) >= 0;
        }

        void LoadFavorites()
        {
            favs.Clear();
            try
            {
                string f = Path.Combine(dataDir, "_favorites.txt");
                if (File.Exists(f))
                    foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length < 2) continue;
                        if (File.Exists(PagePath(p[0], p[1])) && favs.IndexOf(FavKey(p[0], p[1])) < 0) favs.Add(FavKey(p[0], p[1]));
                    }
            }
            catch { }
            FillFavorites();
        }

        void SaveFavorites()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string k in favs) sb.Append(k).Append("\r\n");
            try { File.WriteAllText(Path.Combine(dataDir, "_favorites.txt"), sb.ToString(), Encoding.UTF8); } catch { }
            FillFavorites();
        }

        void FillFavorites()
        {
            favList.BeginUpdate();
            favList.Items.Clear();
            foreach (string k in favs) favList.Items.Add(k);
            favList.EndUpdate();
            favList.SelectedIndex = -1;
            int rows = Math.Min(Math.Max(favs.Count, 1), 6);
            favPanel.Height = favHead.Height + rows * favList.ItemHeight + Px(4);
            favPanel.Visible = favs.Count > 0;
            pgList.Invalidate();
        }

        void ToggleFavorite()
        {
            if (curNotebook == null || curPage == null) return;
            string k = FavKey(curNotebook, curPage);
            if (favs.IndexOf(k) >= 0) { favs.Remove(k); stLeft.Text = "즐겨찾기에서 뺐습니다: " + curPage; }
            else { favs.Add(k); stLeft.Text = "즐겨찾기에 추가했습니다: " + curPage; }
            SaveFavorites();
        }

        void RenameFav(string nb, string pg, string nb2, string pg2)
        {
            int i = favs.IndexOf(FavKey(nb, pg));
            if (i < 0) return;
            favs[i] = FavKey(nb2, pg2);
            SaveFavorites();
        }

        void RemoveFav(string nb, string pg)
        {
            if (favs.Remove(FavKey(nb, pg))) SaveFavorites();
        }

        // 노트북 이름이 바뀌거나(새 이름) 삭제되면(null) 즐겨찾기도 맞춤
        void RenameFavNotebook(string nb, string nb2)
        {
            bool changed = false;
            for (int i = favs.Count - 1; i >= 0; i--)
            {
                string[] p = favs[i].Split('\t');
                if (p[0] != nb) continue;
                if (nb2 == null) favs.RemoveAt(i);
                else favs[i] = FavKey(nb2, p[1]);
                changed = true;
            }
            if (changed) SaveFavorites();
        }

        void FavList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= favList.Items.Count) return;
            string[] p = favList.Items[e.Index].ToString().Split('\t');
            bool cur = p.Length > 1 && p[0] == curNotebook && p[1] == curPage;
            using (SolidBrush b = new SolidBrush(cur ? cSel : favList.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
            int x = e.Bounds.X + Px(12);
            TextRenderer.DrawText(e.Graphics, "★", favList.Font, new Rectangle(x - Px(2), e.Bounds.Y, Px(16), e.Bounds.Height),
                Color.FromArgb(230, 170, 0), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += Px(16);
            int nbW = Px(64);
            Rectangle r = new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x - nbW - Px(4), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, p.Length > 1 ? p[1] : "", favList.Font, r, cText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Rectangle nr = new Rectangle(e.Bounds.Right - nbW - Px(4), e.Bounds.Y, nbW, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, p[0], favSmallFont, nr, cMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        void FavList_MouseUp(object sender, MouseEventArgs e)
        {
            int i = favList.IndexFromPoint(e.Location);
            if (i < 0) return;
            favList.SelectedIndex = i;
            if (e.Button != MouseButtons.Left) return;
            string[] p = favs[i].Split('\t');
            GoToPage(p[0], p[1]);
            favList.SelectedIndex = -1;
            favList.Invalidate();
        }

        string FavSelected(out string page)
        {
            page = null;
            int i = favList.SelectedIndex;
            if (i < 0 || i >= favs.Count) return null;
            string[] p = favs[i].Split('\t');
            page = p[1];
            return p[0];
        }

        void FavRemove_Click(object sender, EventArgs e)
        {
            string pg;
            string nb = FavSelected(out pg);
            if (nb != null) RemoveFav(nb, pg);
        }

        void FavSticky_Click(object sender, EventArgs e)
        {
            string pg;
            string nb = FavSelected(out pg);
            if (nb != null) OpenSticky(nb, pg, false, Point.Empty);
        }

        void FavToggle_Click(object sender, EventArgs e) { ToggleFavorite(); }

        void GoToPage(string nb, string pg)
        {
            if (IndexOfName(nbOrder, nb) < 0) return;
            if (nb != curNotebook)
            {
                FillList(nbList, nbOrder, nb);
                OpenNotebook(nb, pg);
            }
            else if (pg != curPage)
            {
                FillList(pgList, pgOrder, pg);
                OpenPage(pg);
            }
            favList.Invalidate();
        }

        // ================= 포스트잇 =================

        StickyNote FindSticky(string nb, string pg)
        {
            if (nb == null || pg == null) return null;
            foreach (StickyNote sn in stickies)
                if (string.Equals(sn.Notebook, nb, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(sn.Page, pg, StringComparison.OrdinalIgnoreCase)) return sn;
            return null;
        }

        // 포스트잇이 열려 있으면 이름 바꾸기·이동·삭제를 막음 (pg 가 null 이면 노트북 전체)
        bool StickyBlocks(string nb, string pg)
        {
            foreach (StickyNote sn in stickies)
            {
                if (!string.Equals(sn.Notebook, nb, StringComparison.OrdinalIgnoreCase)) continue;
                if (pg != null && !string.Equals(sn.Page, pg, StringComparison.OrdinalIgnoreCase)) continue;
                MessageBox.Show(this, "'" + sn.Page + "' 페이지가 포스트잇으로 열려 있습니다.\n포스트잇을 먼저 닫아 주세요.", "AHNNOTE");
                sn.Activate();
                return true;
            }
            return false;
        }

        Color PageBgOf(string nb, string pg)
        {
            Dictionary<string, string> vals = pgBg;
            if (nb != curNotebook)
            {
                vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ReadMeta(PgMetaFile(nb), vals);
            }
            string v;
            int argb;
            if (vals.TryGetValue(pg, out v) && int.TryParse(v, out argb)) return Color.FromArgb(argb);
            return Color.White;
        }

        void OpenSticky(string nb, string pg, bool hasPos, Point pos)
        {
            StickyNote ex = FindSticky(nb, pg);
            if (ex != null)
            {
                if (hasPos) ex.Location = pos;
                if (ex.WindowState == FormWindowState.Minimized) ex.WindowState = FormWindowState.Normal;
                ex.Activate();
                return;
            }
            string path = PagePath(nb, pg);
            if (!File.Exists(path)) return;
            if (nb == curNotebook && pg == curPage) { CommitTitle(); SaveCurrent(); }
            StickyNote sn = new StickyNote(nb, pg, path, PageBgOf(nb, pg), baseFont, S);
            if (hasPos) sn.Location = new Point(pos.X - Px(40), pos.Y - Px(12));
            else sn.Location = new Point(Right - sn.Width - Px(40), Top + Px(120) + stickies.Count * Px(28));
            sn.KeepOnScreen();
            AddSticky(sn);
            sn.Show();
            sn.Activate();
        }

        void AddSticky(StickyNote sn)
        {
            sn.FormClosed += new FormClosedEventHandler(Sticky_Closed);
            sn.LayoutChangedByUser += new EventHandler(Sticky_Layout);
            sn.BackToMain += new EventHandler(Sticky_BackToMain);
            stickies.Add(sn);
            SaveStickies();
            ApplyLock();
            pgList.Invalidate();
        }

        void Sticky_Closed(object sender, FormClosedEventArgs e)
        {
            if (appClosing) return;
            StickyNote sn = (StickyNote)sender;
            stickies.Remove(sn);
            SaveStickies();
            pgList.Invalidate();
            // 메인에서 보고 있던 페이지면 포스트잇에서 고친 내용으로 다시 열기
            if (sn.Notebook == curNotebook && sn.Page == curPage) OpenPage(curPage);
        }

        void Sticky_Layout(object sender, EventArgs e) { if (!appClosing) SaveStickies(); }

        void Sticky_BackToMain(object sender, EventArgs e)
        {
            StickyNote sn = (StickyNote)sender;
            string nb = sn.Notebook, pg = sn.Page;
            sn.Close();
            GoToPage(nb, pg);
            Activate();
            ed.Focus();
        }

        void SaveStickies()
        {
            StringBuilder sb = new StringBuilder();
            foreach (StickyNote sn in stickies)
            {
                Rectangle b = sn.WindowState == FormWindowState.Normal ? sn.Bounds : sn.RestoreBounds;
                sb.Append(sn.Notebook).Append('\t').Append(sn.Page).Append('\t')
                  .Append(b.X).Append('\t').Append(b.Y).Append('\t').Append(b.Width).Append('\t').Append(b.Height).Append('\t')
                  .Append(sn.Pinned ? "1" : "0").Append('\t')
                  .Append(sn.NoteColor.IsEmpty ? 0 : sn.NoteColor.ToArgb()).Append("\r\n");
            }
            try { File.WriteAllText(Path.Combine(dataDir, "_stickies.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        void RestoreStickies()
        {
            try
            {
                string f = Path.Combine(dataDir, "_stickies.txt");
                if (!File.Exists(f)) return;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 7) continue;
                    string path = PagePath(p[0], p[1]);
                    if (!File.Exists(path) || FindSticky(p[0], p[1]) != null) continue;
                    int x, y, w, h;
                    int.TryParse(p[2], out x); int.TryParse(p[3], out y);
                    int.TryParse(p[4], out w); int.TryParse(p[5], out h);
                    StickyNote sn = new StickyNote(p[0], p[1], path, PageBgOf(p[0], p[1]), baseFont, S);
                    if (w > 50 && h > 50) sn.Bounds = new Rectangle(x, y, w, h);
                    sn.KeepOnScreen();
                    sn.SetPinned(p[6] == "1");
                    int nc;
                    if (p.Length > 7 && int.TryParse(p[7], out nc) && nc != 0) { sn.NoteColor = Color.FromArgb(nc); sn.ApplyColors(); }
                    AddSticky(sn);
                    sn.Show();
                }
            }
            catch { }
            Activate();
        }

        // 포스트잇으로 열린 페이지는 메인에서 읽기 전용
        void ApplyLock()
        {
            bool locked = curPage != null && FindSticky(curNotebook, curPage) != null;
            ed.ReadOnly = locked;
            titleBox.ReadOnly = locked;
            lockBar.Visible = locked;
            if (locked) ed.BackColor = pagePanel.BackColor;   // 읽기 전용이어도 배경색 유지
        }

        void LockShow_Click(object sender, EventArgs e)
        {
            StickyNote sn = FindSticky(curNotebook, curPage);
            if (sn != null) { if (sn.WindowState == FormWindowState.Minimized) sn.WindowState = FormWindowState.Normal; sn.Activate(); }
        }

        void LockBack_Click(object sender, EventArgs e)
        {
            StickyNote sn = FindSticky(curNotebook, curPage);
            if (sn != null) sn.Close();
            ed.Focus();
        }

        void StickyOpen_Click(object sender, EventArgs e)
        {
            if (curPage != null) OpenSticky(curNotebook, curPage, false, Point.Empty);
        }

        // ---------- 페이지를 메인 창 밖으로 끌어서 포스트잇으로 ----------

        void PgList_DragDown(object sender, MouseEventArgs e)
        {
            dragIndex = e.Button == MouseButtons.Left ? pgList.IndexFromPoint(e.Location) : -1;
            dragStart = e.Location;
        }

        void PgList_DragUp(object sender, MouseEventArgs e) { dragIndex = -1; }

        void PgList_DragMove(object sender, MouseEventArgs e)
        {
            if (dragIndex < 0 || e.Button != MouseButtons.Left) return;
            Size ds = SystemInformation.DragSize;
            if (Math.Abs(e.X - dragStart.X) < ds.Width * 2 && Math.Abs(e.Y - dragStart.Y) < ds.Height * 2) return;
            if (dragIndex >= pgOrder.Count) { dragIndex = -1; return; }
            dragPage = pgOrder[dragIndex];
            dragIndex = -1;
            detachNow = false;
            pgList.DoDragDrop(dragPage, DragDropEffects.Move);
            if (detachNow && dragPage != null && curNotebook != null) OpenSticky(curNotebook, dragPage, true, detachAt);
        }

        void PgList_QueryDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed) { e.Action = DragAction.Cancel; return; }
            if ((e.KeyState & 1) == 0)   // 마우스 버튼을 놓음
            {
                e.Action = DragAction.Cancel;
                Point p = Control.MousePosition;
                if (!Bounds.Contains(p)) { detachNow = true; detachAt = p; }
            }
        }

        void PgList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Cursor.Current = Bounds.Contains(Control.MousePosition) ? Cursors.No : Cursors.Hand;
        }

        // ================= QR 로 보기 =================

        void Qr_Click(object sender, EventArgs e) { ShowQr(); }

        void ShowQr()
        {
            if (curNotebook == null || curPage == null) return;
            CommitTitle();
            SaveCurrent();
            StickyNote sn = FindSticky(curNotebook, curPage);
            if (sn != null) sn.SaveNow();
            string text = "";
            using (RichTextBox tmp = new RichTextBox())
            {
                try { tmp.LoadFile(PagePath(curNotebook, curPage), RichTextBoxStreamType.RichText); text = tmp.Text; }
                catch { text = ed.Text; }
            }
            using (QrForm f = new QrForm(curPage, text, S))
            {
                f.TopMost = TopMost;
                f.ShowDialog(this);
            }
            SaveSettings();   // 고른 양, 단축어 링크 기억
        }

        // ================= 캘린더 / 알림 =================

        void CalBtn_Click(object sender, EventArgs e) { OpenCalendar(DateTime.Today, false); }

        void OpenCalendar(DateTime day, bool jump)
        {
            if (calForm == null || calForm.IsDisposed)
            {
                calForm = new CalendarForm(cal, S);
                calForm.Changed += new EventHandler(CalForm_Changed);
                calForm.FormClosed += new FormClosedEventHandler(CalForm_Closed);
                calForm.TopMost = TopMost;
                calForm.StartPosition = FormStartPosition.Manual;
                calForm.Location = new Point(Left + Math.Max(0, (Width - calForm.Width) / 2), Top + Math.Max(0, (Height - calForm.Height) / 2));
                calForm.Show(this);
                jump = true;
            }
            if (jump) calForm.GoTo(day);
            if (calForm.WindowState == FormWindowState.Minimized) calForm.WindowState = FormWindowState.Normal;
            calForm.Activate();
        }

        void CalForm_Changed(object sender, EventArgs e) { UpdateCalButton(); }

        void CalForm_Closed(object sender, FormClosedEventArgs e) { calForm = null; }

        void UpdateCalButton()
        {
            int n = cal.EventsOn(DateTime.Today).Count;
            calBtn.Text = "  ▦  캘린더" + (n > 0 ? "      오늘 " + n + "개" : "");
        }

        void AlarmTimer_Tick(object sender, EventArgs e)
        {
            CheckAlarms(false);
            UpdateCalButton();
        }

        // 알림 시각이 지난 것을 찾아 팝업. 시작할 때는 꺼져 있던 동안 놓친 알림을 모아서 보여줌.
        void CheckAlarms(bool startup)
        {
            DateTime now = DateTime.Now;
            List<string> missed = new List<string>();
            bool changed = false;
            foreach (CalEvent ev in cal.Events)
            {
                DateTime b = ev.Base();
                foreach (int m in ev.Alarms)
                {
                    if (ev.Fired.IndexOf(m) >= 0) continue;
                    DateTime t = b.AddMinutes(-m);
                    if (t > now) continue;
                    ev.Fired.Add(m);
                    changed = true;
                    if ((now - t).TotalMinutes <= 3 && !startup)
                    {
                        ShowAlarm(ev, m);
                    }
                    else if (b >= now.AddDays(-7))
                    {
                        missed.Add(ev.Date.ToString("M/d (ddd) ") + ev.TimeText() + "  " + ev.Title + "  (" + EventForm.AlarmText(m) + ")");
                    }
                }
            }
            if (changed) cal.Save();
            if (missed.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < missed.Count && i < 7; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(missed[i]);
                }
                if (missed.Count > 7) sb.Append("\n외 " + (missed.Count - 7) + "개");
                string head = startup ? "놓친 알림 " + missed.Count + "개" : "지난 알림 " + missed.Count + "개";
                string title = startup ? "꺼져 있는 동안 지난 알림" : "알림 시각이 지났습니다";
                AlarmPopup p = new AlarmPopup(head, title, sb.ToString(), DateTime.Today, S);
                p.OpenCalendar += new EventHandler(Popup_Open);
                p.Show();
            }
        }

        void ShowAlarm(CalEvent ev, int m)
        {
            string detail = ev.Date.ToString("M월 d일 (ddd)  ") + ev.TimeText();
            if (ev.Body.Trim().Length > 0)
            {
                string first = ev.Body.Trim().Replace("\r\n", "\n").Split('\n')[0];
                detail += "\n" + first;
            }
            AlarmPopup p = new AlarmPopup(EventForm.AlarmText(m) + " 알림", ev.Title, detail, ev.Date, S);
            p.OpenCalendar += new EventHandler(Popup_Open);
            p.Show();
        }

        void Popup_Open(object sender, EventArgs e)
        {
            AlarmPopup p = (AlarmPopup)sender;
            OpenCalendar(p.Target, true);
        }

        // ================= 편집기 =================

        void Ed_SelectionChanged(object sender, EventArgs e)
        {
            if (!uiBusy && !ed.Busy && !loading) UpdateToolbar();
        }

        void Ed_FormatChanged(object sender, EventArgs e) { UpdateToolbar(); }

        void UpdateToolbar()
        {
            uiBusy = true;
            Font f = ed.SelFont();
            boldBtn.Checked = f != null && f.Bold;
            italicBtn.Checked = f != null && f.Italic;
            underBtn.Checked = f != null && f.Underline;
            strikeBtn.Checked = f != null && f.Strikeout;
            sizeBox.Text = f != null ? f.Size.ToString("0.#") : "";
            uiBusy = false;
        }

        void UpdateStatus()
        {
            if (curNotebook == null) return;
            Text = "AHNNOTE - " + curNotebook + " / " + curPage;
            string s = curNotebook + "  ›  " + curPage;
            if (lastSaved.Length > 0) s += "      ✔ 저장됨 " + lastSaved;
            stLeft.Text = s;
            stRight.Text = "글자 수 " + ed.TextLength + "   확대 " + (int)Math.Round(ed.ZoomFactor * 100) + "%";
        }

        // ================= 찾기 =================

        void ShowFind(bool replace)
        {
            if (findForm == null)
            {
                findForm = new FindForm(this, ed, S);
                Skin.Apply(findForm);
            }
            findForm.TopMost = TopMost;
            string sel = ed.SelectedText;
            if (sel.Length > 0 && sel.IndexOf('\n') < 0 && sel.Length < 100) findForm.SetFindText(sel);
            findForm.Open(replace);
        }

        void FindAgain(bool forward)
        {
            if (findForm == null || findForm.FindText.Length == 0) { ShowFind(false); return; }
            findForm.FindNext(forward);
        }

        // ================= 시작 페이지 =================

        void AddText(string text, float size, FontStyle st, Color fore, Color hi)
        {
            ed.Select(ed.TextLength, 0);
            ed.SelectionFont = new Font(FONT_NAME, size, st);
            ed.SelectionColor = ColorMap.Disp(fore);
            if (hi.IsEmpty) ed.ClearHighlight();
            else ed.SelectionBackColor = ColorMap.Disp(hi);
            ed.SelectedText = text;
        }

        void AddLine(string text) { AddText(text + "\n", BASE_SIZE, FontStyle.Regular, Color.Black, Color.Empty); }

        void WriteWelcome()
        {
            Color none = Color.Empty;
            ed.Clear();
            AddText("AHNNOTE에 오신 것을 환영합니다\n", 20f, FontStyle.Bold, Color.Black, none);
            AddLine("가벼운 노트 앱입니다. 왼쪽에서 노트북, 가운데에서 페이지를 고르세요. 내용은 자동 저장됩니다.");
            AddLine("");
            AddText("서식 단축키\n", 16f, FontStyle.Bold, Color.Black, none);
            AddText("Ctrl+B", BASE_SIZE, FontStyle.Bold, Color.Black, none); AddLine("  굵게");
            AddText("Ctrl+I", BASE_SIZE, FontStyle.Italic, Color.Black, none); AddLine("  기울임");
            AddText("Ctrl+U", BASE_SIZE, FontStyle.Underline, Color.Black, none); AddLine("  밑줄");
            AddText("Ctrl+-", BASE_SIZE, FontStyle.Strikeout, Color.Black, none); AddLine("  취소선");
            AddText("글자색", BASE_SIZE, FontStyle.Regular, Color.FromArgb(224, 62, 62), none);
            AddText(" / ", BASE_SIZE, FontStyle.Regular, Color.Black, none);
            AddText("형광펜", BASE_SIZE, FontStyle.Regular, Color.Black, Color.FromArgb(255, 241, 118));
            AddText(" / 배경 : 위 도구 모음에서 선택\n", BASE_SIZE, FontStyle.Regular, Color.Black, none);
            AddLine("Ctrl+Alt+1~3  제목 1~3,  Ctrl+Alt+0  본문,  Ctrl+Shift+> <  글자 크기");
            AddLine("Alt+1~9  상용구 넣기 (블록 잡고 우클릭 → 선택한 글을 상용구로 저장)");
            AddLine("Ctrl+Shift+C  최근 글자색,  Ctrl+Shift+H  최근 형광펜 (한 번 더 누르면 원래대로),  우클릭 메뉴에서 색 고르기");
            AddLine("");
            AddText("기본 단축키\n", 16f, FontStyle.Bold, Color.Black, none);
            AddLine("Ctrl+F 찾기,  Ctrl+H 바꾸기,  F3 다음 찾기");
            AddLine("Ctrl+A 모두 선택,  Ctrl+C 복사,  Ctrl+X 잘라내기,  Ctrl+V 붙여넣기,  Ctrl+Shift+V 텍스트만 붙여넣기");
            AddLine("Ctrl+Z 실행 취소,  Ctrl+Y 다시 실행,  Ctrl+S 저장");
            AddLine("Ctrl+N 새 페이지,  Ctrl+Shift+N 새 노트북,  F2 이름 바꾸기,  Alt+↑↓ 순서 바꾸기");
            AddLine("Ctrl+마우스 휠 확대/축소,  Ctrl+0 원래 크기,  Ctrl+Shift+D 다크 모드,  Ctrl+Shift+T 항상 위");
            AddLine("");
            AddText("자동 변환 (입력하면 바로 바뀝니다)\n", 16f, FontStyle.Bold, Color.Black, none);
            AddLine("[]  →  체크박스,   [x]  →  체크됨,   ()  →  ○");
            AddLine("->  →  →,   <-  →  ←,   <->  →  ↔,   =>  →  ⇒,   <=>  →  ⇔");
            AddLine("줄 처음에  #  ##  ###  + 스페이스  →  제목 1, 2, 3");
            AddLine("줄 처음에  -  + 스페이스  →  • 글머리 목록");
            AddLine("---  + 엔터  →  구분선");
            AddLine("");
            AddText("할 일 목록 (네모를 클릭해 보세요)\n", 13f, FontStyle.Bold, Color.Black, none);
            ed.Select(ed.TextLength, 0); ed.SelectionFont = baseFont; ed.ClearHighlight();
            ed.InsertCheckbox(); AddLine("AHNNOTE 빌드하기");
            ed.Select(ed.TextLength, 0); ed.SelectionFont = baseFont;
            ed.InsertCheckbox(); AddLine("노트북 만들기");
            ed.Select(ed.TextLength, 0); ed.SelectionFont = baseFont;
            ed.InsertCheckbox(); AddLine("Ctrl+1 로 체크박스 넣기");
            int first = ed.Text.IndexOf('\u2610');
            if (first >= 0) ed.ToggleCheckAt(first, '☐');
            ed.Select(0, 0);
            ed.ClearUndo();
        }

        // ================= 이벤트 연결 =================

        void NewNotebook_Click(object sender, EventArgs e) { NewNotebook(); }
        void NewPage_Click(object sender, EventArgs e) { NewPage(); }
        void RenameNotebook_Click(object sender, EventArgs e) { RenameNotebook(); }
        void DeleteNotebook_Click(object sender, EventArgs e) { DeleteNotebook(); }
        void NbUp_Click(object sender, EventArgs e) { MoveNotebook(-1); }
        void NbDown_Click(object sender, EventArgs e) { MoveNotebook(1); }
        void RenamePage_Click(object sender, EventArgs e) { RenamePage(); }
        void DeletePage_Click(object sender, EventArgs e) { DeletePage(); }
        void PgUp_Click(object sender, EventArgs e) { MovePage(-1); }
        void PgDown_Click(object sender, EventArgs e) { MovePage(1); }
        void MoveTo_Click(object sender, EventArgs e) { MovePageTo((string)((ToolStripItem)sender).Tag); }
        void Bold_Click(object sender, EventArgs e) { ed.ToggleStyle(FontStyle.Bold); }
        void Italic_Click(object sender, EventArgs e) { ed.ToggleStyle(FontStyle.Italic); }
        void Under_Click(object sender, EventArgs e) { ed.ToggleStyle(FontStyle.Underline); }
        void Strike_Click(object sender, EventArgs e) { ed.ToggleStyle(FontStyle.Strikeout); }
        void Todo_Click(object sender, EventArgs e) { ed.ToggleTodo(); ed.Focus(); }
        void Bullet_Click(object sender, EventArgs e) { ed.ToggleBullet(); ed.Focus(); }
        void Find_Click(object sender, EventArgs e) { ShowFind(false); }
        void Cut_Click(object sender, EventArgs e) { ed.Cut(); }
        void Copy_Click(object sender, EventArgs e) { ed.Copy(); }
        void Paste_Click(object sender, EventArgs e) { ed.PasteRich(); }
        void PastePlain_Click(object sender, EventArgs e) { ed.PastePlain(); }
        void SelectAll_Click(object sender, EventArgs e) { ed.SelectAll(); }

        void Heading_Click(object sender, EventArgs e)
        {
            ed.SetHeading((int)((ToolStripItem)sender).Tag);
            ed.Focus();
        }

        void ApplySizeBox()
        {
            float v;
            if (float.TryParse(sizeBox.Text, out v) && v >= 1f && v <= 200f)
            {
                ed.ApplyFont(NoteEditor.MODE_SIZE, FontStyle.Regular, false, v);
                ed.Focus();
            }
        }

        void SizeBox_Changed(object sender, EventArgs e) { if (!uiBusy) ApplySizeBox(); }

        void SizeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplySizeBox(); }
        }

        Color PickColor(object tag, Color current)
        {
            if (tag is Color) return (Color)tag;
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true;
                cd.Color = ColorMap.Disp(current);
                if (cd.ShowDialog(this) == DialogResult.OK) return dark ? ColorMap.ToLight(cd.Color) : cd.Color;
            }
            return Color.Transparent;
        }

        void ForeBtn_Click(object sender, EventArgs e)
        {
            ed.ApplyFore(NoteEditor.RecentFore, false);
            ed.Focus();
        }

        void ForePick_Click(object sender, EventArgs e)
        {
            Color c = PickColor(((ToolStripItem)sender).Tag, NoteEditor.RecentFore);
            if (c == Color.Transparent) return;
            ed.ApplyFore(c, true);
            ed.Focus();
        }

        void HiBtn_Click(object sender, EventArgs e)
        {
            ed.ApplyHighlight(NoteEditor.RecentHi, false);
            ed.Focus();
        }

        // 최근 색이 바뀌면 도구 모음 색 표시와 설정 저장
        void Recent_Changed(object sender, EventArgs e)
        {
            foreBtn.Image = MakeSwatch(NoteEditor.RecentFore);
            hiBtn.Image = MakeSwatch(NoteEditor.RecentHi);
            SaveSettings();
        }

        void HiPick_Click(object sender, EventArgs e)
        {
            object tag = ((ToolStripItem)sender).Tag;
            if (tag is Color && ((Color)tag).IsEmpty)
            {
                ed.ClearHighlight();
                ed.Focus();
                return;
            }
            Color c = PickColor(tag, NoteEditor.RecentHi);
            if (c == Color.Transparent) return;
            ed.ApplyHighlight(c, true);
            ed.Focus();
        }

        void BgPick_Click(object sender, EventArgs e)
        {
            Color c = PickColor(((ToolStripItem)sender).Tag, ed.BackColor);
            if (c == Color.Transparent) return;
            SetPageBg(c);
            ed.Focus();
        }
    }

    // ================= 다크 모드 색 바꾸기 =================
    // 화면에 보여줄 때만 밝기를 뒤집고, 파일에는 항상 라이트 기준 색으로 저장

    public static class ColorMap
    {
        public static bool Dark = false;
        static Dictionary<int, int> darkToLight = new Dictionary<int, int>();

        public static Encoding Latin1() { return Encoding.GetEncoding(28591); }

        // 화면에 보여줄 색 (다크 모드면 밝기를 뒤집음). 저장은 항상 라이트 기준 색.
        public static Color Disp(Color c)
        {
            if (!Dark || c.IsEmpty) return c;
            Color d = ToDarkColor(c);
            darkToLight[d.ToArgb()] = c.ToArgb();
            return d;
        }

        public static Color ToLight(Color d)
        {
            int v;
            if (darkToLight.TryGetValue(d.ToArgb(), out v)) return Color.FromArgb(v);
            if (d.GetBrightness() < 0.10f) return d;   // 붙여넣은 아주 어두운 색은 그대로
            float l = 1f - (d.GetBrightness() - 0.10f) / 0.80f;
            return FromHsl(d.GetHue(), d.GetSaturation(), l);
        }

        static Color ToDarkColor(Color c)
        {
            float l = 0.10f + 0.80f * (1f - c.GetBrightness());
            return FromHsl(c.GetHue(), c.GetSaturation(), l);
        }

        static Color FromHsl(float h, float s, float l)
        {
            if (l < 0f) l = 0f;
            if (l > 1f) l = 1f;
            if (s <= 0.001f)
            {
                int v = ToByte(l);
                return Color.FromArgb(v, v, v);
            }
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            float hk = h / 360f;
            return Color.FromArgb(ToByte(HueToRgb(p, q, hk + 1f / 3f)), ToByte(HueToRgb(p, q, hk)), ToByte(HueToRgb(p, q, hk - 1f / 3f)));
        }

        static float HueToRgb(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        static int ToByte(float v)
        {
            int i = (int)Math.Round(v * 255f);
            if (i < 0) i = 0;
            if (i > 255) i = 255;
            return i;
        }

        static readonly Regex COLOR_RE = new Regex(@"\\red(\d+)\\green(\d+)\\blue(\d+)");

        // RTF 색상표의 색을 다크용 / 라이트용으로 바꿈
        public static string MapColorTable(string rtf, bool toDark)
        {
            int i = rtf.IndexOf("{\\colortbl");
            if (i < 0) return rtf;
            int j = rtf.IndexOf('}', i);
            if (j < 0) return rtf;
            string tbl = rtf.Substring(i, j - i);
            StringBuilder sb = new StringBuilder();
            int pos = 0;
            foreach (Match m in COLOR_RE.Matches(tbl))
            {
                sb.Append(tbl, pos, m.Index - pos);
                Color c = Color.FromArgb(Math.Min(255, int.Parse(m.Groups[1].Value)),
                    Math.Min(255, int.Parse(m.Groups[2].Value)), Math.Min(255, int.Parse(m.Groups[3].Value)));
                Color n = toDark ? Disp(c) : ToLight(c);
                sb.Append("\\red").Append(n.R).Append("\\green").Append(n.G).Append("\\blue").Append(n.B);
                pos = m.Index + m.Length;
            }
            sb.Append(tbl.Substring(pos));
            return rtf.Substring(0, i) + sb.ToString() + rtf.Substring(j);
        }
    }

    // ================= 편집기 (메인 창과 포스트잇이 같이 씀) =================

    public class NoteEditor : RichTextBox
    {
        public const string FONT_NAME = "Malgun Gothic";
        public const string SYM_FONT = "Segoe UI Symbol";
        public const float BASE_SIZE = 11f;
        public const string BOX = "☐";
        public const string BOX_ON = "☑";
        public const string BULLET = "• ";
        public const int MODE_STYLE = 0;
        public const int MODE_SIZE = 1;
        public const int MODE_GROW = 2;

        // 자동 변환 규칙 (긴 것부터 검사)
        static readonly string[] RULE_FROM = { "<=>", "←>", "[ ]", "[]", "[x]", "[X]", "[v]", "()", "->", "<-", "=>" };
        static readonly string[] RULE_TO = { "⇔", "↔", BOX, BOX, BOX_ON, BOX_ON, BOX_ON, "○", "→", "←", "⇒" };

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        static extern IntPtr SendCharFormat(IntPtr h, int msg, IntPtr w, ref CHARFORMAT2 cf);
        const int WM_SETREDRAW = 0x000B;
        const int EM_SETCHARFORMAT = 0x0444;
        const int EM_GETCHARFORMAT = 0x043A;
        const int SCF_SELECTION = 0x0001;
        const uint CFM_BACKCOLOR = 0x04000000;
        const uint CFM_COLOR = 0x40000000;
        const uint CFE_AUTOCOLOR = 0x40000000;

        public Font BaseFont;
        public float UiScale = 1f;
        public bool Dirty = false;
        public bool Loading = false;
        public bool Busy = false;
        public event EventHandler FormatChanged;

        public NoteEditor(Font baseFont, float scale)
        {
            BaseFont = baseFont;
            UiScale = scale;
            Font = baseFont;
            BorderStyle = BorderStyle.None;
            AcceptsTab = true;
            HideSelection = false;
            DetectUrls = true;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            LanguageOption = RichTextBoxLanguageOptions.UIFonts; // 한글 입력 시 글꼴이 바뀌는 문제 방지
        }

        void OnFormatChanged()
        {
            if (FormatChanged != null) FormatChanged(this, EventArgs.Empty);
        }

        protected override void OnTextChanged(EventArgs e) { MarkDirty(); base.OnTextChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { Ed_KeyDown(this, e); base.OnKeyDown(e); }
        protected override void OnKeyPress(KeyPressEventArgs e) { Ed_KeyPress(this, e); base.OnKeyPress(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Ed_MouseUp(this, e); base.OnMouseUp(e); }
        protected override void OnLinkClicked(LinkClickedEventArgs e) { Ed_LinkClicked(this, e); base.OnLinkClicked(e); }

        // 편집 단축키
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ReadOnly)
            {
                if (keyData == (Keys.Control | Keys.A)) { SelectAll(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            switch (keyData)
            {
                case Keys.Control | Keys.B: ToggleStyle(FontStyle.Bold); return true;
                case Keys.Control | Keys.I: ToggleStyle(FontStyle.Italic); return true;
                case Keys.Control | Keys.U: ToggleStyle(FontStyle.Underline); return true;
                case Keys.Control | Keys.OemMinus: ToggleStyle(FontStyle.Strikeout); return true;
                case Keys.Control | Keys.A: SelectAll(); return true;
                case Keys.Control | Keys.Shift | Keys.V: PastePlain(); return true;
                case Keys.Control | Keys.V: PasteRich(); return true;
                case Keys.Shift | Keys.Insert: PasteRich(); return true;
                case Keys.Control | Keys.Y: Redo(); return true;
                case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
                case Keys.Control | Keys.D1: ToggleTodo(); return true;
                case Keys.Control | Keys.OemPeriod: ToggleBullet(); return true;
                case Keys.Control | Keys.Alt | Keys.D1: SetHeading(1); return true;
                case Keys.Control | Keys.Alt | Keys.D2: SetHeading(2); return true;
                case Keys.Control | Keys.Alt | Keys.D3: SetHeading(3); return true;
                case Keys.Control | Keys.Alt | Keys.D0: SetHeading(0); return true;
                case Keys.Control | Keys.Shift | Keys.OemPeriod: ApplyFont(MODE_GROW, FontStyle.Regular, false, 1f); return true;
                case Keys.Control | Keys.Shift | Keys.Oemcomma: ApplyFont(MODE_GROW, FontStyle.Regular, false, -1f); return true;
                case Keys.Control | Keys.D0: ZoomFactor = 1f; return true;
                case Keys.Control | Keys.Shift | Keys.C: ToggleRecentFore(); return true;
                case Keys.Control | Keys.Shift | Keys.H: ToggleRecentHi(); return true;
            }
            // Alt+1~9 : 상용구 넣기
            if ((keyData & Keys.Modifiers) == Keys.Alt)
            {
                Keys k = keyData & Keys.KeyCode;
                if (k >= Keys.D1 && k <= Keys.D9)
                {
                    Snippet sn = Snippets.ByKey(k - Keys.D0);
                    if (sn != null) Snippets.Insert(this, sn);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 페이지 파일 열기 (다크 모드면 색을 바꿔서 보여줌)
        public void LoadPage(string path)
        {
            Loading = true;
            try
            {
                if (!File.Exists(path)) Clear();
                else if (!ColorMap.Dark) LoadFile(path, RichTextBoxStreamType.RichText);
                else
                {
                    string rtf = File.ReadAllText(path, ColorMap.Latin1());
                    if (!rtf.StartsWith("{\\rtf")) throw new Exception("not rtf");
                    Rtf = ColorMap.MapColorTable(rtf, true);
                }
            }
            catch
            {
                try { LoadFile(path, RichTextBoxStreamType.PlainText); } catch { Clear(); }
            }
            if (TextLength == 0)
            {
                SelectAll();
                SelectionFont = BaseFont;
                SelectionColor = ColorMap.Disp(Color.Black);
            }
            else if (ColorMap.Dark) FixTextColors(0, TextLength, false, 0);
            Select(0, 0);
            ClearUndo();
            Modified = false;
            Dirty = false;
            Loading = false;
        }

        // 페이지 파일 저장. 성공하면 null, 실패하면 오류 내용
        public string SavePage(string path)
        {
            string tmp = path + ".tmp";
            try
            {
                if (!ColorMap.Dark) SaveFile(tmp, RichTextBoxStreamType.RichText);
                else File.WriteAllText(tmp, ColorMap.MapColorTable(Rtf, false), ColorMap.Latin1());
                DateTime created = File.Exists(path) ? File.GetCreationTime(path) : DateTime.Now;
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                try { File.SetCreationTime(path, created); } catch { }
                Dirty = false;
                Modified = false;
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // 다크 모드: 색 지정이 없는(자동) 글자를 밝은 기본색으로.
        // fixDark 이면 붙여넣은 어두운 글자색도 다크용으로 바꿈.
        public void FixTextColors(int start, int end, bool fixDark, int caret)
        {
            Color text = ColorMap.Disp(Color.Black);
            Busy = true;
            SetRedraw(false);
            try
            {
                int i = start;
                while (i < end)
                {
                    int n = end - i;
                    Select(i, n);
                    CHARFORMAT2 cf = GetCharFormat();
                    while (n > 1 && (cf.dwMask & CFM_COLOR) == 0)
                    {
                        n = n / 2;
                        Select(i, n);
                        cf = GetCharFormat();
                    }
                    if ((cf.dwMask & CFM_COLOR) != 0)
                    {
                        if ((cf.dwEffects & CFE_AUTOCOLOR) != 0) SelectionColor = text;
                        else if (fixDark && SelectionColor.GetBrightness() < 0.35f) SelectionColor = ColorMap.Disp(SelectionColor);
                    }
                    i += n;
                }
            }
            catch
            {
                // Win32 호출이 안 되는 환경용 (느리지만 안전)
                for (int k = start; k < end; k++)
                {
                    Select(k, 1);
                    if (SelectionColor.GetBrightness() < 0.35f) SelectionColor = ColorMap.Disp(SelectionColor);
                }
            }
            Select(caret, 0);
            if (TextLength == 0) SelectionColor = text;
            SetRedraw(true);
            Busy = false;
        }

        // 붙여넣기 (다크 모드면 글자색 맞춤)
        public void PasteRich()
        {
            if (ReadOnly) return;
            int s0 = SelectionStart;
            Paste();
            if (!ColorMap.Dark) return;
            int s1 = SelectionStart;
            if (s1 > s0) FixTextColors(s0, s1, true, s1);
        }

        CHARFORMAT2 GetCharFormat()
        {
            CHARFORMAT2 cf = new CHARFORMAT2();
            cf.cbSize = Marshal.SizeOf(typeof(CHARFORMAT2));
            cf.szFaceName = "";
            SendCharFormat(Handle, EM_GETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
            return cf;
        }

        // 선택 영역 글꼴 (글꼴이 섞여 있으면 null)
        public Font SelFont()
        {
            try { return SelectionFont; }
            catch { return null; }
        }

        public void MarkDirty()
        {
            if (!Loading) Dirty = true;
        }

        Font MakeFont(Font f, int mode, FontStyle st, bool on, float val)
        {
            FontStyle ns = f.Style;
            float sz = f.Size;
            if (mode == MODE_STYLE) ns = on ? (ns | st) : (ns & ~st);
            else if (mode == MODE_SIZE) sz = val;
            else if (mode == MODE_GROW) sz = Math.Max(6f, Math.Min(96f, f.Size + val));
            try { return new Font(f.FontFamily, sz, ns); }
            catch { return f; }
        }

        void SetRedraw(bool on)
        {
            try
            {
                SendMessage(Handle, WM_SETREDRAW, (IntPtr)(on ? 1 : 0), IntPtr.Zero);
                if (on) Invalidate();
            }
            catch { }
        }

        // 선택 영역에 글꼴 변경 적용 (글꼴이 섞여 있어도 각각 유지)
        public void ApplyFont(int mode, FontStyle st, bool on, float val)
        {
            if (ReadOnly) return;
            int s = SelectionStart, len = SelectionLength;
            Font f = SelFont();
            if (len == 0 || f != null)
            {
                if (f == null) f = BaseFont;
                SelectionFont = MakeFont(f, mode, st, on, val);
            }
            else
            {
                Busy = true;
                SetRedraw(false);
                int i = s, end = s + len;
                while (i < end)
                {
                    int n = end - i;
                    Select(i, n);
                    while (n > 1 && SelFont() == null)
                    {
                        n = n / 2;
                        Select(i, n);
                    }
                    Font cf = SelFont();
                    if (cf != null) SelectionFont = MakeFont(cf, mode, st, on, val);
                    i += n;
                }
                Select(s, len);
                SetRedraw(true);
                Busy = false;
            }
            MarkDirty();
            OnFormatChanged();
        }

        public void ToggleStyle(FontStyle st)
        {
            if (ReadOnly) return;
            Font f = SelFont();
            bool on;
            if (f != null) on = (f.Style & st) == 0;
            else
            {
                int s = SelectionStart, len = SelectionLength;
                Busy = true;
                Select(s, 1);
                Font f0 = SelFont();
                Select(s, len);
                Busy = false;
                on = f0 == null || (f0.Style & st) == 0;
            }
            ApplyFont(MODE_STYLE, st, on, 0f);
            Focus();
        }

        // ---------- 글자색 / 형광펜 ----------

        public static readonly Color[] FORE_COLORS = { Color.Black, Color.FromArgb(224, 62, 62), Color.FromArgb(217, 115, 13), Color.FromArgb(203, 145, 47),
            Color.FromArgb(15, 123, 108), Color.FromArgb(11, 107, 203), Color.FromArgb(105, 64, 165), Color.FromArgb(155, 154, 151) };
        public static readonly string[] FORE_NAMES = { "기본", "빨강", "주황", "노랑", "초록", "파랑", "보라", "회색" };
        public static readonly Color[] HI_COLORS = { Color.FromArgb(255, 241, 118), Color.FromArgb(197, 225, 165), Color.FromArgb(179, 229, 252),
            Color.FromArgb(248, 187, 208), Color.FromArgb(255, 204, 128), Color.FromArgb(225, 190, 231), Color.FromArgb(224, 224, 224) };
        public static readonly string[] HI_NAMES = { "노랑", "연두", "하늘", "분홍", "주황", "보라", "회색" };

        // 단축키로 칠할 색 (가장 최근에 고른 색, 처음엔 빨강 / 노랑)
        public static Color RecentFore = Color.FromArgb(224, 62, 62);
        public static Color RecentHi = Color.FromArgb(255, 241, 118);
        public static event EventHandler RecentChanged;

        static void SetRecent(bool fore, Color c)
        {
            if (fore) RecentFore = c; else RecentHi = c;
            if (RecentChanged != null) RecentChanged(null, EventArgs.Empty);
        }

        // 색은 항상 라이트 기준 색으로 받음 (다크 모드면 화면용으로 바꿔서 칠함)
        public void ApplyFore(Color light, bool remember)
        {
            if (ReadOnly) return;
            if (remember && light.ToArgb() != Color.Black.ToArgb()) SetRecent(true, light);
            SelectionColor = ColorMap.Disp(light);
            MarkDirty();
        }

        public void ApplyHighlight(Color light, bool remember)
        {
            if (ReadOnly) return;
            if (remember) SetRecent(false, light);
            SelectionBackColor = ColorMap.Disp(light);
            MarkDirty();
        }

        // Ctrl+Shift+C : 최근 글자색. 이미 그 색이면 기본색으로
        public void ToggleRecentFore()
        {
            if (ReadOnly) return;
            Color d = ColorMap.Disp(RecentFore);
            if (SelectionLength > 0 && SelectionColor.ToArgb() == d.ToArgb()) SelectionColor = ColorMap.Disp(Color.Black);
            else SelectionColor = d;
            MarkDirty();
        }

        // Ctrl+Shift+H : 최근 형광펜. 이미 그 색이면 지우기
        public void ToggleRecentHi()
        {
            if (ReadOnly) return;
            Color d = ColorMap.Disp(RecentHi);
            if (SelectionLength > 0 && SelectionBackColor.ToArgb() == d.ToArgb()) ClearHighlight();
            else { SelectionBackColor = d; MarkDirty(); }
        }

        Bitmap Swatch(Color c)
        {
            int n = (int)(16 * UiScale);
            Bitmap b = new Bitmap(n, n);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                using (SolidBrush br = new SolidBrush(c)) g.FillRectangle(br, 1, 1, n - 2, n - 2);
                g.DrawRectangle(Pens.Gray, 1, 1, n - 3, n - 3);
            }
            return b;
        }

        ToolStripMenuItem foreMenu, hiMenu;

        // 우클릭 메뉴에 글자색 / 형광펜 넣기
        public void AddColorMenus(ContextMenuStrip m)
        {
            foreMenu = new ToolStripMenuItem("글자색 (Ctrl+Shift+C)");
            for (int i = 0; i < FORE_COLORS.Length; i++)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem(FORE_NAMES[i], Swatch(FORE_COLORS[i]), new EventHandler(MenuFore_Click));
                mi.Tag = FORE_COLORS[i];
                foreMenu.DropDownItems.Add(mi);
            }
            foreMenu.DropDownItems.Add(new ToolStripMenuItem("다른 색...", null, new EventHandler(MenuFore_Click)));
            hiMenu = new ToolStripMenuItem("형광펜 (Ctrl+Shift+H)");
            for (int i = 0; i < HI_COLORS.Length; i++)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem(HI_NAMES[i], Swatch(HI_COLORS[i]), new EventHandler(MenuHi_Click));
                mi.Tag = HI_COLORS[i];
                hiMenu.DropDownItems.Add(mi);
            }
            hiMenu.DropDownItems.Add(new ToolStripMenuItem("다른 색...", null, new EventHandler(MenuHi_Click)));
            ToolStripMenuItem none = new ToolStripMenuItem("형광펜 없음", null, new EventHandler(MenuHi_Click));
            none.Tag = Color.Empty;
            hiMenu.DropDownItems.Add(none);
            m.Items.Insert(0, new ToolStripSeparator());
            m.Items.Insert(0, hiMenu);
            m.Items.Insert(0, foreMenu);
            m.Opening += new System.ComponentModel.CancelEventHandler(ColorMenu_Opening);
        }

        void ColorMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            foreMenu.Enabled = !ReadOnly;
            hiMenu.Enabled = !ReadOnly;
        }

        Color AskColor(Color current)
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true;
                cd.Color = ColorMap.Disp(current);
                if (cd.ShowDialog(FindForm()) != DialogResult.OK) return Color.Empty;
                return ColorMap.Dark ? ColorMap.ToLight(cd.Color) : cd.Color;
            }
        }

        void MenuFore_Click(object sender, EventArgs e)
        {
            object tag = ((ToolStripItem)sender).Tag;
            Color c = tag is Color ? (Color)tag : AskColor(RecentFore);
            if (c.IsEmpty) return;
            ApplyFore(c, true);
            Focus();
        }

        void MenuHi_Click(object sender, EventArgs e)
        {
            object tag = ((ToolStripItem)sender).Tag;
            if (tag is Color && ((Color)tag).IsEmpty) { ClearHighlight(); Focus(); return; }
            Color c = tag is Color ? (Color)tag : AskColor(RecentHi);
            if (c.IsEmpty) return;
            ApplyHighlight(c, true);
            Focus();
        }

        public void ClearHighlight()
        {
            if (ReadOnly) return;
            try
            {
                CHARFORMAT2 cf = new CHARFORMAT2();
                cf.cbSize = Marshal.SizeOf(typeof(CHARFORMAT2));
                cf.dwMask = CFM_BACKCOLOR;
                cf.dwEffects = CFM_BACKCOLOR; // CFE_AUTOBACKCOLOR
                cf.szFaceName = "";
                SendCharFormat(Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
            }
            catch
            {
                SelectionBackColor = BackColor;
            }
            MarkDirty();
        }

        public static int LineStart(string t, int pos)
        {
            if (pos > t.Length) pos = t.Length;
            while (pos > 0 && t[pos - 1] != '\n') pos--;
            return pos;
        }

        public static int LineEnd(string t, int pos)
        {
            while (pos < t.Length && t[pos] != '\n') pos++;
            return pos;
        }

        static bool Near(float a, float b) { return Math.Abs(a - b) < 0.1f; }

        static float HeadingSize(int level)
        {
            if (level == 1) return 20f;
            if (level == 2) return 16f;
            if (level == 3) return 13f;
            return BASE_SIZE;
        }

        public static bool IsHeadingFont(Font f)
        {
            return f != null && f.Bold && (Near(f.Size, 20f) || Near(f.Size, 16f) || Near(f.Size, 13f));
        }

        // 현재 줄을 제목 1~3 / 본문(0)으로
        public void SetHeading(int level)
        {
            if (ReadOnly) return;
            int s = SelectionStart, len = SelectionLength;
            string t = Text;
            int ls = LineStart(t, s);
            int le = LineEnd(t, Math.Min(s + len, t.Length));
            Font hf = new Font(FONT_NAME, HeadingSize(level), level == 0 ? FontStyle.Regular : FontStyle.Bold);
            Busy = true;
            Select(ls, le - ls);
            SelectionFont = hf;
            Select(s, len);
            if (len == 0) SelectionFont = hf;
            Busy = false;
            MarkDirty();
            OnFormatChanged();
        }

        public void InsertCheckbox()
        {
            Font cur = SelFont();
            if (cur == null) cur = BaseFont;
            SelectionFont = new Font(SYM_FONT, cur.Size, cur.Style & ~FontStyle.Underline);
            SelectedText = BOX;
            SelectionFont = cur;
            SelectedText = " ";
        }

        public void ToggleCheckAt(int idx, char c)
        {
            int s0 = SelectionStart, l0 = SelectionLength;
            Busy = true;
            Select(idx, 1);
            SelectedText = (c == '☐') ? BOX_ON : BOX;
            Select(s0, l0);
            Busy = false;
            MarkDirty();
        }

        // 줄 맨 앞에 커서를 두고, 그 줄 글자와 같은 모양으로 맞춤
        void CaretToLineStart(string t, int ls)
        {
            Font f = SelFont();
            Color c = SelectionColor;
            if (ls < t.Length && t[ls] != '\n')
            {
                Select(ls, 1);
                Font f2 = SelFont();
                if (f2 != null) { f = f2; c = SelectionColor; }
            }
            if (f == null) f = BaseFont;
            Select(ls, 0);
            SelectionFont = f;
            SelectionColor = c;
        }

        public void ToggleTodo()
        {
            if (ReadOnly) return;
            string t = Text;
            int s0 = SelectionStart, l0 = SelectionLength;
            int ls = LineStart(t, s0);
            if (ls < t.Length && (t[ls] == '☐' || t[ls] == '☑'))
            {
                ToggleCheckAt(ls, t[ls]);
            }
            else
            {
                CaretToLineStart(t, ls);
                InsertCheckbox();
                Select(s0 + 2, l0);
            }
            MarkDirty();
        }

        public void ToggleBullet()
        {
            if (ReadOnly) return;
            string t = Text;
            int s0 = SelectionStart, l0 = SelectionLength;
            int ls = LineStart(t, s0);
            if (t.Length >= ls + 2 && t.Substring(ls, 2) == BULLET)
            {
                Select(ls, 2);
                SelectedText = "";
                Select(Math.Max(ls, s0 - 2), l0);
            }
            else
            {
                CaretToLineStart(t, ls);
                SelectedText = BULLET;
                Select(s0 + 2, l0);
            }
            MarkDirty();
        }

        public void PastePlain()
        {
            if (ReadOnly) return;
            try
            {
                if (Clipboard.ContainsText())
                    SelectedText = Clipboard.GetText().Replace("\r\n", "\n");
            }
            catch { }
        }

        // 엔터: 제목 다음 줄은 본문, 목록/할일 이어쓰기, --- 는 구분선
        void Ed_KeyDown(object sender, KeyEventArgs e)
        {
            if (ReadOnly) return;
            if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None || SelectionLength > 0) return;
            int pos = SelectionStart;
            string t = Text;
            if (pos > t.Length) return;
            int ls = LineStart(t, pos);
            int le = LineEnd(t, pos);
            string line = t.Substring(ls, le - ls);

            if (line == "---" && pos == le)
            {
                e.SuppressKeyPress = true;
                Select(ls, 3);
                SelectedText = new string('─', 30) + "\n";
                SelectionFont = BaseFont;
                MarkDirty();
                return;
            }

            bool isBullet = line.StartsWith(BULLET);
            bool isTodo = line.Length >= 2 && (line[0] == '☐' || line[0] == '☑') && line[1] == ' ';
            if ((isBullet || isTodo) && pos >= ls + 2)
            {
                e.SuppressKeyPress = true;
                Font keepFont = SelFont();
                if (keepFont == null || IsHeadingFont(keepFont)) keepFont = BaseFont;
                Color keepColor = SelectionColor;
                if (line.Length == 2)
                {
                    // 빈 항목에서 엔터 → 목록 끝내기
                    Select(ls, 2);
                    SelectedText = "";
                    SelectionFont = keepFont;
                    SelectionColor = keepColor;
                }
                else
                {
                    SelectedText = "\n";
                    // 새 줄은 문서 끝 글자 모양(예: 제목)을 따라가므로 지금 글자 모양으로 다시 지정
                    SelectionFont = keepFont;
                    SelectionColor = keepColor;
                    if (isBullet) SelectedText = BULLET;
                    else InsertCheckbox();
                }
                MarkDirty();
                return;
            }

            if (pos == le && IsHeadingFont(SelFont()))
            {
                e.SuppressKeyPress = true;
                SelectedText = "\n";
                SelectionFont = BaseFont;
                MarkDirty();
            }
        }

        // 입력하면서 바로 바꾸기 ([] → ☐, -> → →, # 제목 등)
        void Ed_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (ReadOnly) return;
            if (SelectionLength > 0) return;
            char c = e.KeyChar;
            int pos = SelectionStart;

            if (c == ' ')
            {
                int vls = GetFirstCharIndexOfCurrentLine();
                int n = pos - vls;
                if (n < 1 || n > 3) return;
                string t0 = Text;
                if (pos > t0.Length) return;
                int ls = LineStart(t0, pos);
                string before = t0.Substring(ls, pos - ls);
                if (before == "#" || before == "##" || before == "###")
                {
                    e.Handled = true;
                    Select(ls, before.Length);
                    SelectedText = "";
                    SetHeading(before.Length);
                }
                else if (before == "-" || before == "*")
                {
                    e.Handled = true;
                    Select(ls, 1);
                    SelectedText = BULLET;
                    MarkDirty();
                }
                return;
            }

            if (c != ']' && c != ')' && c != '>' && c != '-') return;
            string t = Text;
            if (pos > t.Length) return;
            int from = Math.Max(0, pos - 3);
            string typed = t.Substring(from, pos - from) + c;

            for (int i = 0; i < RULE_FROM.Length; i++)
            {
                string rf = RULE_FROM[i];
                if (!typed.EndsWith(rf)) continue;
                e.Handled = true;
                Font cur = SelFont();
                if (cur == null) cur = BaseFont;
                SelectedText = c.ToString();   // 먼저 입력 (Ctrl+Z 하면 원래 글자로)
                int start = pos + 1 - rf.Length;
                Select(start, rf.Length);
                bool box = RULE_TO[i] == BOX || RULE_TO[i] == BOX_ON;
                if (box) SelectionFont = new Font(SYM_FONT, cur.Size, cur.Style & ~FontStyle.Underline);
                SelectedText = RULE_TO[i];
                if (box) SelectionFont = cur;
                MarkDirty();
                return;
            }
        }

        // 체크박스 클릭하면 ☐ ↔ ☑
        void Ed_MouseUp(object sender, MouseEventArgs e)
        {
            if (ReadOnly) return;
            if (e.Button != MouseButtons.Left || SelectionLength > 0) return;
            int idx = GetCharIndexFromPosition(e.Location);
            string t = Text;
            if (idx < 0 || idx >= t.Length) return;
            char c = t[idx];
            if (c != '☐' && c != '☑') return;
            Point p = GetPositionFromCharIndex(idx);
            int w = (int)(16 * UiScale);
            if (idx + 1 < t.Length)
            {
                Point p2 = GetPositionFromCharIndex(idx + 1);
                if (p2.Y == p.Y && p2.X > p.X) w = p2.X - p.X;
            }
            if (e.X >= p.X - 1 && e.X <= p.X + w && e.Y >= p.Y) ToggleCheckAt(idx, c);
        }

        void Ed_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(e.LinkText); } catch { }
        }
    }

    // ================= 다크 모드 그리기 =================

    public static class Skin
    {
        public static bool Dark = false;
        public static Color Back = SystemColors.Control;
        public static Color Fore = SystemColors.ControlText;
        public static Color Field = SystemColors.Window;
        public static Color Line = SystemColors.ControlDark;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr h, string app, string id);

        public static void SetDark(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Back = Color.FromArgb(43, 43, 45);
                Fore = Color.FromArgb(232, 232, 232);
                Field = Color.FromArgb(32, 32, 32);
                Line = Color.FromArgb(80, 80, 84);
            }
            else
            {
                Back = SystemColors.Control;
                Fore = SystemColors.ControlText;
                Field = SystemColors.Window;
                Line = SystemColors.ControlDark;
            }
        }

        public static void TitleBar(Form f, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, 20, ref v, 4);
            }
            catch { }
        }

        public static void ScrollBars(Control c, bool dark)
        {
            try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch { }
        }

        // 대화 상자 색 맞추기
        public static void Apply(Control root)
        {
            if (root is Form)
            {
                root.BackColor = Back;
                root.ForeColor = Fore;
                TitleBar((Form)root, Dark);
            }
            foreach (Control c in root.Controls)
            {
                if (c is TextBox)
                {
                    c.BackColor = Field;
                    c.ForeColor = Fore;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    if (Dark)
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = Line;
                        b.BackColor = Field;
                        b.ForeColor = Fore;
                    }
                    else
                    {
                        b.FlatStyle = FlatStyle.Standard;
                        b.BackColor = SystemColors.Control;
                        b.ForeColor = SystemColors.ControlText;
                        b.UseVisualStyleBackColor = true;
                    }
                }
                else if (c is Label || c is CheckBox)
                {
                    if (c.ForeColor == SystemColors.ControlText || c.ForeColor == Color.FromArgb(232, 232, 232)) c.ForeColor = Fore;
                }
                if (c.Controls.Count > 0) Apply(c);
            }
        }
    }

    // 도구 모음 / 메뉴 색
    public class AhnColors : ProfessionalColorTable
    {
        Color bar, menu, hover, press, check, border, sep;

        public AhnColors(bool dark)
        {
            UseSystemColors = false;
            if (dark)
            {
                bar = Color.FromArgb(43, 43, 45);
                menu = Color.FromArgb(43, 43, 45);
                hover = Color.FromArgb(62, 62, 66);
                press = Color.FromArgb(52, 78, 110);
                check = Color.FromArgb(38, 72, 110);
                border = Color.FromArgb(70, 70, 74);
                sep = Color.FromArgb(70, 70, 74);
            }
            else
            {
                bar = Color.White;
                menu = Color.White;
                hover = Color.FromArgb(229, 240, 251);
                press = Color.FromArgb(204, 224, 247);
                check = Color.FromArgb(204, 224, 247);
                border = Color.FromArgb(200, 206, 214);
                sep = Color.FromArgb(214, 218, 224);
            }
        }

        public override Color ToolStripGradientBegin { get { return bar; } }
        public override Color ToolStripGradientMiddle { get { return bar; } }
        public override Color ToolStripGradientEnd { get { return bar; } }
        public override Color ToolStripBorder { get { return bar; } }
        public override Color ToolStripDropDownBackground { get { return menu; } }
        public override Color ImageMarginGradientBegin { get { return menu; } }
        public override Color ImageMarginGradientMiddle { get { return menu; } }
        public override Color ImageMarginGradientEnd { get { return menu; } }
        public override Color MenuBorder { get { return border; } }
        public override Color MenuItemBorder { get { return hover; } }
        public override Color MenuItemSelected { get { return hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return hover; } }
        public override Color MenuItemPressedGradientBegin { get { return press; } }
        public override Color MenuItemPressedGradientMiddle { get { return press; } }
        public override Color MenuItemPressedGradientEnd { get { return press; } }
        public override Color ButtonSelectedHighlight { get { return hover; } }
        public override Color ButtonSelectedBorder { get { return hover; } }
        public override Color ButtonSelectedGradientBegin { get { return hover; } }
        public override Color ButtonSelectedGradientMiddle { get { return hover; } }
        public override Color ButtonSelectedGradientEnd { get { return hover; } }
        public override Color ButtonPressedHighlight { get { return press; } }
        public override Color ButtonPressedBorder { get { return press; } }
        public override Color ButtonPressedGradientBegin { get { return press; } }
        public override Color ButtonPressedGradientMiddle { get { return press; } }
        public override Color ButtonPressedGradientEnd { get { return press; } }
        public override Color ButtonCheckedHighlight { get { return check; } }
        public override Color ButtonCheckedGradientBegin { get { return check; } }
        public override Color ButtonCheckedGradientMiddle { get { return check; } }
        public override Color ButtonCheckedGradientEnd { get { return check; } }
        public override Color CheckBackground { get { return check; } }
        public override Color CheckSelectedBackground { get { return press; } }
        public override Color CheckPressedBackground { get { return press; } }
        public override Color SeparatorDark { get { return sep; } }
        public override Color SeparatorLight { get { return bar; } }
        public override Color StatusStripGradientBegin { get { return bar; } }
        public override Color StatusStripGradientEnd { get { return bar; } }
    }

    public class AhnRenderer : ToolStripProfessionalRenderer
    {
        Color text;

        public AhnRenderer(ProfessionalColorTable t, Color textColor) : base(t)
        {
            text = textColor;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? text : Color.Gray;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = text;
            base.OnRenderArrow(e);
        }
    }

    // ================= 포스트잇 창 =================

    public class StickyNote : Form
    {
        static readonly Color YELLOW = Color.FromArgb(255, 244, 160);
        public string Notebook, Page, PathFile;
        public NoteEditor Ed;
        public bool Pinned = false;
        public bool AppClosing = false;
        public Color PageBg;
        Color noteColor = Color.Empty;
        public Color NoteColor { get { return noteColor; } set { noteColor = value; } }   // 포스트잇 전용 색 (비어 있으면 자동)
        public event EventHandler LayoutChangedByUser;
        public event EventHandler BackToMain;
        Panel bar, inner;
        Label nameLabel;
        Button pinBtn, backBtn, colorBtn, closeBtn;
        ContextMenuStrip colorMenu;
        bool dragging = false;
        Point dragMouse, dragForm;

        static readonly Color[] NOTE_COLORS = { Color.FromArgb(255, 244, 160), Color.FromArgb(255, 205, 220), Color.FromArgb(200, 230, 255),
            Color.FromArgb(215, 240, 190), Color.FromArgb(230, 215, 250), Color.FromArgb(255, 220, 180), Color.FromArgb(232, 232, 232), Color.White };
        static readonly string[] NOTE_NAMES = { "노랑", "분홍", "하늘", "연두", "보라", "주황", "회색", "흰색" };
        Timer saveTimer;
        ToolTip tip;
        float S;
        bool mdl;   // 윈도우 아이콘 글꼴이 있으면 아이콘, 없으면 글자

        int Px(int v) { return (int)(v * S); }

        public StickyNote(string nb, string page, string path, Color bg, Font baseFont, float scale)
        {
            Notebook = nb;
            Page = page;
            PathFile = path;
            PageBg = bg;
            S = scale;
            Text = page;
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.None;     // 윈도우 제목 표시줄 숨김
            Padding = new Padding(Px(3));               // 이 테두리를 끌어서 크기 조절
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Px(320), Px(320));
            MinimumSize = new Size(Px(180), Px(130));
            KeyPreview = true;

            tip = new ToolTip();
            using (Font test = new Font("Segoe MDL2 Assets", 10f)) mdl = test.Name == "Segoe MDL2 Assets";
            bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = Px(30);
            nameLabel = new Label();
            nameLabel.Dock = DockStyle.Fill;
            nameLabel.TextAlign = ContentAlignment.MiddleLeft;
            nameLabel.AutoEllipsis = true;
            nameLabel.Padding = new Padding(Px(6), 0, 0, 0);
            nameLabel.Text = nb + "  ›  " + page;
            nameLabel.Cursor = Cursors.SizeAll;
            colorBtn = BarButton(mdl ? "\uE790" : "색", "포스트잇 색");
            colorBtn.Click += new EventHandler(ColorBtn_Click);
            pinBtn = BarButton(mdl ? "\uE718" : "핀", "항상 위에 고정");
            pinBtn.Click += new EventHandler(Pin_Click);
            backBtn = BarButton(mdl ? "\uE8A7" : "↩", "메인 창으로 돌려보내기 (포스트잇 닫기)");
            backBtn.Click += new EventHandler(Back_Click);
            closeBtn = BarButton(mdl ? "\uE8BB" : "×", "포스트잇 닫기");
            closeBtn.Click += new EventHandler(Close_Click);
            bar.Controls.Add(nameLabel);
            bar.Controls.Add(colorBtn);
            bar.Controls.Add(pinBtn);
            bar.Controls.Add(backBtn);
            bar.Controls.Add(closeBtn);
            // 위쪽 줄을 끌어서 옮기기
            bar.MouseDown += new MouseEventHandler(Bar_MouseDown);
            bar.MouseMove += new MouseEventHandler(Bar_MouseMove);
            bar.MouseUp += new MouseEventHandler(Bar_MouseUp);
            nameLabel.MouseDown += new MouseEventHandler(Bar_MouseDown);
            nameLabel.MouseMove += new MouseEventHandler(Bar_MouseMove);
            nameLabel.MouseUp += new MouseEventHandler(Bar_MouseUp);

            colorMenu = new ContextMenuStrip();
            for (int i = 0; i < NOTE_COLORS.Length; i++)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem(NOTE_NAMES[i], Swatch(NOTE_COLORS[i]), new EventHandler(NoteColor_Click));
                mi.Tag = NOTE_COLORS[i];
                colorMenu.Items.Add(mi);
            }
            colorMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem auto = new ToolStripMenuItem("기본 (페이지 배경색 따르기)", null, new EventHandler(NoteColor_Click));
            auto.Tag = Color.Empty;
            colorMenu.Items.Add(auto);

            inner = new Panel();
            inner.Dock = DockStyle.Fill;
            inner.Padding = new Padding(Px(10), Px(6), Px(6), Px(6));
            Ed = new NoteEditor(baseFont, scale);
            Ed.Dock = DockStyle.Fill;
            inner.Controls.Add(Ed);

            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("잘라내기 (Ctrl+X)", null, new EventHandler(Cut_Click));
            m.Items.Add("복사 (Ctrl+C)", null, new EventHandler(Copy_Click));
            m.Items.Add("붙여넣기 (Ctrl+V)", null, new EventHandler(Paste_Click));
            m.Items.Add("텍스트만 붙여넣기 (Ctrl+Shift+V)", null, new EventHandler(PastePlain_Click));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("모두 선택 (Ctrl+A)", null, new EventHandler(SelectAll_Click));
            Ed.AddColorMenus(m);
            new SnippetMenu(Ed, baseFont, scale).Attach(m);
            Ed.ContextMenuStrip = m;

            Controls.Add(inner);
            Controls.Add(bar);

            Ed.LoadPage(path);
            ApplyColors();
            SetPinned(false);

            saveTimer = new Timer();
            saveTimer.Interval = 3000;
            saveTimer.Tick += new EventHandler(SaveTimer_Tick);
            saveTimer.Start();
        }

        Button BarButton(string glyph, string tipText)
        {
            Button b = new Button();
            b.Dock = DockStyle.Right;
            b.Width = Px(32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = mdl ? new Font("Segoe MDL2 Assets", 10f) : new Font("Malgun Gothic", 9f, FontStyle.Bold);
            b.Text = glyph;
            b.TabStop = false;
            b.Cursor = Cursors.Hand;
            tip.SetToolTip(b, tipText);
            return b;
        }

        public void SetPinned(bool on)
        {
            Pinned = on;
            TopMost = on;
            if (mdl) pinBtn.Text = on ? "\uE841" : "\uE718";   // 고정됨 / 고정 핀
            else pinBtn.Text = "핀";
            Color accent = Skin.Dark ? Color.FromArgb(96, 180, 255) : Color.FromArgb(0, 103, 192);
            pinBtn.BackColor = on ? accent : bar.BackColor;
            pinBtn.ForeColor = on ? Color.White : nameLabel.ForeColor;
            tip.SetToolTip(pinBtn, on ? "고정 해제" : "항상 위에 고정");
        }

        Bitmap Swatch(Color c)
        {
            int n = Px(16);
            Bitmap b = new Bitmap(n, n);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                using (SolidBrush br = new SolidBrush(c)) g.FillRectangle(br, 1, 1, n - 2, n - 2);
                g.DrawRectangle(Pens.Gray, 1, 1, n - 3, n - 3);
            }
            return b;
        }

        // 다크 모드에서는 같은 색을 어둡게
        static Color DarkTint(Color c)
        {
            return Color.FromArgb((int)(c.R * 0.28 + 30 * 0.72), (int)(c.G * 0.28 + 30 * 0.72), (int)(c.B * 0.28 + 32 * 0.72));
        }

        public void ApplyColors()
        {
            Color light;
            if (!NoteColor.IsEmpty) light = NoteColor;
            else light = PageBg.ToArgb() == Color.White.ToArgb() ? YELLOW : PageBg;
            Color d = Skin.Dark ? DarkTint(light) : light;
            Ed.BackColor = d;
            inner.BackColor = d;
            BackColor = d;
            // 위쪽 줄은 조금 진하게
            int k = Skin.Dark ? 18 : -22;
            bar.BackColor = Color.FromArgb(Clamp(d.R + k), Clamp(d.G + k), Clamp(d.B + k));
            nameLabel.ForeColor = Skin.Dark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(90, 80, 40);
            Color hover = Color.FromArgb(Clamp(bar.BackColor.R + k), Clamp(bar.BackColor.G + k), Clamp(bar.BackColor.B + k));
            foreach (Button b in new Button[] { backBtn, colorBtn, closeBtn })
            {
                b.BackColor = bar.BackColor;
                b.ForeColor = nameLabel.ForeColor;
                b.FlatAppearance.MouseOverBackColor = hover;
            }
            pinBtn.FlatAppearance.MouseOverBackColor = hover;
            BackColor = hover;   // 바깥 테두리
            SetPinnedColorsOnly();
            Skin.TitleBar(this, Skin.Dark);
        }

        void SetPinnedColorsOnly()
        {
            if (pinBtn == null) return;
            Color accent = Skin.Dark ? Color.FromArgb(96, 180, 255) : Color.FromArgb(0, 103, 192);
            pinBtn.BackColor = Pinned ? accent : bar.BackColor;
            pinBtn.ForeColor = Pinned ? Color.White : nameLabel.ForeColor;
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        public void ReloadTheme()
        {
            Ed.LoadPage(PathFile);
            ApplyColors();
        }

        public void SaveNow()
        {
            if (Ed.Dirty || Ed.Modified) Ed.SavePage(PathFile);
        }

        // 화면 밖에 있으면 안으로
        public void KeepOnScreen()
        {
            Rectangle b = Bounds;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(new Rectangle(b.X, b.Y, b.Width, Px(30)))) return;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - Px(40), wa.Top + Px(80));
        }

        void SaveTimer_Tick(object sender, EventArgs e) { SaveNow(); }

        void Pin_Click(object sender, EventArgs e)
        {
            SetPinned(!Pinned);
            if (LayoutChangedByUser != null) LayoutChangedByUser(this, EventArgs.Empty);
        }

        void Back_Click(object sender, EventArgs e)
        {
            if (BackToMain != null) BackToMain(this, EventArgs.Empty);
        }

        void Close_Click(object sender, EventArgs e) { Close(); }

        void ColorBtn_Click(object sender, EventArgs e)
        {
            colorMenu.Show(colorBtn, new Point(0, colorBtn.Height));
        }

        void NoteColor_Click(object sender, EventArgs e)
        {
            NoteColor = (Color)((ToolStripItem)sender).Tag;
            ApplyColors();
            if (LayoutChangedByUser != null) LayoutChangedByUser(this, EventArgs.Empty);
        }

        // ---------- 위쪽 줄을 끌어서 옮기기 ----------

        void Bar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragMouse = Cursor.Position;
            dragForm = Location;
            ((Control)sender).Capture = true;
        }

        void Bar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point p = Cursor.Position;
            Location = new Point(dragForm.X + p.X - dragMouse.X, dragForm.Y + p.Y - dragMouse.Y);
        }

        void Bar_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            ((Control)sender).Capture = false;
            if (LayoutChangedByUser != null) LayoutChangedByUser(this, EventArgs.Empty);
        }

        // ---------- 테두리를 끌어서 크기 조절 ----------

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            base.WndProc(ref m);
            if (m.Msg != WM_NCHITTEST || (int)m.Result != 1) return;   // 1 = 안쪽
            int lp = m.LParam.ToInt32();
            Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int g = Px(6);
            bool left = p.X < g, right = p.X >= ClientSize.Width - g, top = p.Y < g, bottom = p.Y >= ClientSize.Height - g;
            int r = 0;
            if (top && left) r = 13; else if (top && right) r = 14; else if (bottom && left) r = 16; else if (bottom && right) r = 17;
            else if (left) r = 10; else if (right) r = 11; else if (top) r = 12; else if (bottom) r = 15;
            if (r != 0) m.Result = (IntPtr)r;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (LayoutChangedByUser != null) LayoutChangedByUser(this, EventArgs.Empty);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            SaveNow();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { Ed.Dirty = true; SaveNow(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveNow();
            saveTimer.Stop();
            base.OnFormClosing(e);
        }

        void Cut_Click(object sender, EventArgs e) { Ed.Cut(); }
        void Copy_Click(object sender, EventArgs e) { Ed.Copy(); }
        void Paste_Click(object sender, EventArgs e) { Ed.PasteRich(); }
        void PastePlain_Click(object sender, EventArgs e) { Ed.PastePlain(); }
        void SelectAll_Click(object sender, EventArgs e) { Ed.SelectAll(); }
    }

    // ================= QR 코드 만들기 (외부 라이브러리 없이) =================
    // 바이트 모드 + UTF-8 표시(ECI). 오류 정정 M, 너무 길면 L.

    public class QrCode
    {
        public int Size;
        public bool[,] Dark;     // [y, x]
        bool[,] isFunc;
        int version;
        int ecl;                 // 0=L 1=M

        static readonly int[,] ECC_PER_BLOCK = {
            { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 } };
        static readonly int[,] NUM_BLOCKS = {
            { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 } };
        static readonly int[] FORMAT_ECL = { 1, 0 };   // L=01, M=00

        static int RawModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int na = ver / 7 + 2;
                r -= (25 * na - 10) * na - 55;
                if (ver >= 7) r -= 36;
            }
            return r;
        }

        static int DataCodewords(int ver, int e)
        {
            return RawModules(ver) / 8 - ECC_PER_BLOCK[e, ver] * NUM_BLOCKS[e, ver];
        }

        static int BitsNeeded(int ver, int n)
        {
            return 4 + 8 + 4 + (ver <= 9 ? 8 : 16) + 8 * n;   // ECI(UTF-8) + 바이트 모드 + 길이 + 데이터
        }

        // 이 바이트 수가 QR 하나에 들어가는지
        public static bool Fits(int n)
        {
            return BitsNeeded(40, n) <= DataCodewords(40, 0) * 8;
        }

        public static QrCode Encode(byte[] data)
        {
            for (int e = 1; e >= 0; e--)          // M 먼저, 안 되면 L
                for (int v = 1; v <= 40; v++)
                    if (BitsNeeded(v, data.Length) <= DataCodewords(v, e) * 8)
                        return new QrCode(data, v, e);
            throw new ArgumentException("QR 하나에 담기에는 너무 깁니다.");
        }

        QrCode(byte[] data, int ver, int e)
        {
            version = ver;
            ecl = e;
            Size = ver * 4 + 17;
            Dark = new bool[Size, Size];
            isFunc = new bool[Size, Size];

            // 데이터 비트
            List<bool> bits = new List<bool>();
            AddBits(bits, 7, 4);            // ECI
            AddBits(bits, 26, 8);           // UTF-8
            AddBits(bits, 4, 4);            // 바이트 모드
            AddBits(bits, data.Length, ver <= 9 ? 8 : 16);
            foreach (byte b in data) AddBits(bits, b, 8);
            int cap = DataCodewords(ver, e) * 8;
            AddBits(bits, 0, Math.Min(4, cap - bits.Count));
            AddBits(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) AddBits(bits, pad, 8);
            byte[] codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] all = AddEcc(codewords);
            DrawFunctionPatterns();
            DrawCodewords(all);

            // 가장 보기 좋은 마스크 고르기
            int best = 0, bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int sc = Penalty();
                if (sc < bestScore) { bestScore = sc; best = m; }
                ApplyMask(m);   // 되돌리기
            }
            ApplyMask(best);
            DrawFormatBits(best);
        }

        static void AddBits(List<bool> bits, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0);
        }

        // ---------- 오류 정정 (리드-솔로몬) ----------

        static int GfMul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        static byte[] RsDivisor(int degree)
        {
            byte[] r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < r.Length; j++)
                {
                    r[j] = (byte)GfMul(r[j], root);
                    if (j + 1 < r.Length) r[j] ^= r[j + 1];
                }
                root = GfMul(root, 0x02);
            }
            return r;
        }

        static byte[] RsRemainder(byte[] data, int off, int len, byte[] div)
        {
            byte[] r = new byte[div.Length];
            for (int k = 0; k < len; k++)
            {
                int factor = data[off + k] ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)GfMul(div[i], factor);
            }
            return r;
        }

        byte[] AddEcc(byte[] data)
        {
            int numBlocks = NUM_BLOCKS[ecl, version];
            int eccLen = ECC_PER_BLOCK[ecl, version];
            int raw = RawModules(version) / 8;
            int numShort = numBlocks - raw % numBlocks;
            int shortLen = raw / numBlocks;
            byte[] div = RsDivisor(eccLen);
            byte[][] blocks = new byte[numBlocks][];
            int k = 0;
            for (int i = 0; i < numBlocks; i++)
            {
                int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
                byte[] ecc = RsRemainder(data, k, datLen, div);
                byte[] blk = new byte[shortLen + 1];
                Array.Copy(data, k, blk, 0, datLen);
                k += datLen;
                // 짧은 블록은 datLen 자리를 비워 둠 (아래에서 건너뜀)
                Array.Copy(ecc, 0, blk, shortLen + 1 - eccLen, eccLen);
                blocks[i] = blk;
            }
            byte[] result = new byte[raw];
            int n = 0;
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < numBlocks; j++)
                    if (i != shortLen - eccLen || j >= numShort) result[n++] = blocks[j][i];
            return result;
        }

        // ---------- 고정 무늬 ----------

        void SetFunc(int x, int y, bool dark)
        {
            Dark[y, x] = dark;
            isFunc[y, x] = true;
        }

        void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunc(6, i, i % 2 == 0);
                SetFunc(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);
            int[] pos = AlignPositions();
            int na = pos.Length;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < na; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == na - 1) || (i == na - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            SetFunc(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormatBits(0);
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = version << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = Size - 11 + i % 3, b = i / 3;
                    SetFunc(a, b, bit);
                    SetFunc(b, a, bit);
                }
            }
        }

        void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunc(xx, yy, dist != 2 && dist != 4);
                }
        }

        int[] AlignPositions()
        {
            if (version == 1) return new int[0];
            int na = version / 7 + 2;
            int step = version == 32 ? 26 : (version * 4 + na * 2 + 1) / (na * 2 - 2) * 2;
            int[] r = new int[na];
            r[0] = 6;
            for (int i = na - 1, p = Size - 7; i >= 1; i--, p -= step) r[i] = p;
            return r;
        }

        void DrawFormatBits(int mask)
        {
            int data = FORMAT_ECL[ecl] << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetFunc(8, i, Bit(bits, i));
            SetFunc(8, 7, Bit(bits, 6));
            SetFunc(8, 8, Bit(bits, 7));
            SetFunc(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunc(14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) SetFunc(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetFunc(8, Size - 15 + i, Bit(bits, i));
            SetFunc(8, Size - 8, true);
        }

        static bool Bit(int x, int i) { return ((x >> i) & 1) != 0; }

        void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunc[y, x] && i < data.Length * 8)
                        {
                            Dark[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        void ApplyMask(int m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (isFunc[y, x]) continue;
                    bool inv;
                    switch (m)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) Dark[y, x] = !Dark[y, x];
                }
        }

        // 간단한 점수 (같은 색 줄, 2x2 덩어리, 흑백 비율)
        int Penalty()
        {
            int score = 0;
            for (int y = 0; y < Size; y++)
            {
                int run = 1, runC = 1;
                for (int x = 1; x < Size; x++)
                {
                    if (Dark[y, x] == Dark[y, x - 1]) { run++; if (run == 5) score += 3; else if (run > 5) score++; }
                    else run = 1;
                    if (Dark[x, y] == Dark[x - 1, y]) { runC++; if (runC == 5) score += 3; else if (runC > 5) score++; }
                    else runC = 1;
                }
            }
            int darkCount = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (Dark[y, x]) darkCount++;
                    if (x < Size - 1 && y < Size - 1)
                    {
                        bool c = Dark[y, x];
                        if (c == Dark[y, x + 1] && c == Dark[y + 1, x] && c == Dark[y + 1, x + 1]) score += 3;
                    }
                }
            int total = Size * Size;
            int k = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
            score += Math.Max(0, k) * 10;
            return score;
        }

        // 그림으로 (흰 여백 4칸 포함)
        public Bitmap ToBitmap(int scale)
        {
            int border = 4;
            int n = (Size + border * 2) * scale;
            Bitmap b = new Bitmap(n, n);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (Dark[y, x]) g.FillRectangle(Brushes.Black, (x + border) * scale, (y + border) * scale, scale, scale);
            }
            return b;
        }
    }

    // ================= 페이지를 QR 로 보기 =================

    public class QrForm : Form
    {
        string title, text;
        List<string> parts = new List<string>();
        int index = 0;
        Panel view;
        Label pageLabel, infoLabel;
        Button prevBtn, nextBtn;
        ComboBox sizeBox;
        Bitmap qrBmp;
        float S;
        Button iosBtn, linkBtn;
        bool installMode = false;
        static readonly int[] LIMIT = { 500, 1000, 2000 };   // QR 한 장에 담을 바이트

        // 마지막으로 고른 양 (설정에 저장)
        public static int LastSize = 1;
        // 아이폰 단축어 공유 링크 (설정에서 바꿀 수 있음)
        public const string DEFAULT_IOS_LINK = "https://www.icloud.com/shortcuts/5e838a57c9b245b5bdba42f4e72d77f5";
        public static string IosLink = DEFAULT_IOS_LINK;

        const string GUIDE = "안드로이드: 카메라나 구글 렌즈로 찍어서 글을 복사하세요.\n" +
            "아이폰: 처음 한 번 [아이폰 단축어 설치]로 단축어를 추가한 뒤, 단축어를 실행해서 1장부터 차례로 찍고 다 찍으면 [취소]. 새 메모에 저장됩니다.";
        const string INSTALL_GUIDE = "① 아이폰 카메라로 이 QR을 찍고 [단축어 추가]를 누르세요. (폰 인터넷 필요, 처음 한 번만)\n" +
            "② 다음부터는 단축어 앱에서 방금 추가한 단축어를 실행 → [본문 QR로 돌아가기] 후 1장부터 차례로 찍기 → 다 찍으면 [취소].";

        int Px(int v) { return (int)(v * S); }

        public QrForm(string pageTitle, string pageText, float scale)
        {
            title = pageTitle;
            text = pageText.Replace("\r\n", "\n").TrimEnd();
            S = scale;
            Text = "QR로 보기 - " + pageTitle;
            Font = new Font("Malgun Gothic", 9f);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(Px(560), Px(700));
            MinimumSize = new Size(Px(460), Px(520));

            view = new Panel();
            view.Dock = DockStyle.Fill;
            view.BackColor = Color.White;
            view.Paint += new PaintEventHandler(View_Paint);
            view.Resize += new EventHandler(View_Resize);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Px(150);
            bottom.Padding = new Padding(Px(10), Px(6), Px(10), Px(6));

            prevBtn = new Button();
            prevBtn.Text = "◀ 이전";
            prevBtn.SetBounds(Px(10), Px(8), Px(80), Px(30));
            prevBtn.Click += new EventHandler(Prev_Click);
            pageLabel = new Label();
            pageLabel.TextAlign = ContentAlignment.MiddleCenter;
            pageLabel.Font = new Font("Malgun Gothic", 11f, FontStyle.Bold);
            pageLabel.SetBounds(Px(96), Px(8), Px(110), Px(30));
            nextBtn = new Button();
            nextBtn.Text = "다음 ▶";
            nextBtn.SetBounds(Px(212), Px(8), Px(80), Px(30));
            nextBtn.Click += new EventHandler(Next_Click);
            Label sl = new Label();
            sl.Text = "한 장에";
            sl.TextAlign = ContentAlignment.MiddleRight;
            sl.SetBounds(Px(300), Px(8), Px(56), Px(30));
            sizeBox = new ComboBox();
            sizeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sizeBox.Items.AddRange(new string[] { "적게 (잘 찍힘)", "보통", "많이 (촘촘함)" });
            sizeBox.SelectedIndex = Math.Max(0, Math.Min(2, LastSize));
            sizeBox.SetBounds(Px(360), Px(11), Px(140), Px(24));
            sizeBox.SelectedIndexChanged += new EventHandler(Size_Changed);
            infoLabel = new Label();
            infoLabel.ForeColor = Color.Gray;
            infoLabel.SetBounds(Px(10), Px(44), Px(530), Px(64));
            iosBtn = new Button();
            iosBtn.Text = "아이폰 단축어 설치";
            iosBtn.SetBounds(Px(10), Px(112), Px(170), Px(30));
            iosBtn.Click += new EventHandler(Ios_Click);
            linkBtn = new Button();
            linkBtn.Text = "단축어 링크 바꾸기...";
            linkBtn.SetBounds(Px(186), Px(112), Px(150), Px(30));
            linkBtn.Click += new EventHandler(Link_Click);

            bottom.Controls.Add(prevBtn);
            bottom.Controls.Add(pageLabel);
            bottom.Controls.Add(nextBtn);
            bottom.Controls.Add(sl);
            bottom.Controls.Add(sizeBox);
            bottom.Controls.Add(infoLabel);
            bottom.Controls.Add(iosBtn);
            bottom.Controls.Add(linkBtn);
            Controls.Add(view);
            Controls.Add(bottom);
            Skin.Apply(this);
            view.BackColor = Color.White;   // QR 은 항상 흰 바탕
            Split();
            Show(0);
        }

        // 글을 QR 여러 장으로 나누기 (가능하면 줄 끝에서)
        void Split()
        {
            parts.Clear();
            int limit = LIMIT[sizeBox.SelectedIndex];
            string body = title + "\n\n" + text;
            Encoding u = Encoding.UTF8;
            int pos = 0;
            while (pos < body.Length)
            {
                int lo = pos, hi = body.Length, fit = pos;
                // 들어가는 가장 긴 길이 (앞 번호 자리 20바이트 빼고)
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (u.GetByteCount(body.Substring(pos, mid - pos)) <= limit - 20) { fit = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                if (fit <= pos) fit = Math.Min(body.Length, pos + 1);
                int end = fit;
                if (end < body.Length)
                {
                    int nl = body.LastIndexOf('\n', end - 1, end - pos);
                    int sp = body.LastIndexOf(' ', end - 1, end - pos);
                    if (nl > pos + (end - pos) / 2) end = nl + 1;          // 줄 끝에서 자르기
                    else if (sp > pos + (end - pos) * 3 / 4) end = sp + 1; // 없으면 띄어쓰기에서
                    else if (char.IsLowSurrogate(body[end])) end--;
                }
                parts.Add(body.Substring(pos, end - pos));
                pos = end;
            }
            if (parts.Count == 0) parts.Add(title);
            if (parts.Count > 1)
                for (int i = 0; i < parts.Count; i++) parts[i] = "(" + (i + 1) + "/" + parts.Count + ") " + parts[i];
        }

        void Show(int i)
        {
            index = Math.Max(0, Math.Min(parts.Count - 1, i));
            if (qrBmp != null) { qrBmp.Dispose(); qrBmp = null; }
            try
            {
                QrCode q = QrCode.Encode(Encoding.UTF8.GetBytes(parts[index]));
                qrBmp = q.ToBitmap(1);
                infoLabel.Text = "이 장: " + parts[index].Length + "자 (서식은 빠지고 글자만)" + (parts.Count > 1 ? "   ← → 키로 넘기기" : "") + "\n" + GUIDE;
            }
            catch (Exception ex) { infoLabel.Text = ex.Message; }
            pageLabel.Text = (index + 1) + " / " + parts.Count;
            prevBtn.Enabled = index > 0;
            nextBtn.Enabled = index < parts.Count - 1;
            installMode = false;
            iosBtn.Text = "아이폰 단축어 설치";
            sizeBox.Enabled = true;
            view.Invalidate();
        }

        // 아이폰 단축어 설치 QR 보기 / 본문으로 돌아가기
        void Ios_Click(object sender, EventArgs e)
        {
            if (installMode) { Show(index); return; }
            if (qrBmp != null) { qrBmp.Dispose(); qrBmp = null; }
            try { qrBmp = QrCode.Encode(Encoding.UTF8.GetBytes(IosLink)).ToBitmap(1); } catch { }
            installMode = true;
            pageLabel.Text = "단축어 설치";
            prevBtn.Enabled = false;
            nextBtn.Enabled = false;
            sizeBox.Enabled = false;
            iosBtn.Text = "본문 QR로 돌아가기";
            infoLabel.Text = INSTALL_GUIDE;
            view.Invalidate();
        }

        void Link_Click(object sender, EventArgs e)
        {
            string v = InputDialog.Ask(this, "아이폰 단축어 링크", "iCloud 단축어 공유 링크 (비우면 기본 링크):", IosLink);
            if (v == null) return;
            v = v.Trim();
            if (v.Length == 0) v = DEFAULT_IOS_LINK;
            if (!v.StartsWith("https://")) { MessageBox.Show(this, "https:// 로 시작하는 링크를 넣어 주세요.", "AHNNOTE"); return; }
            IosLink = v;
            if (installMode) { installMode = false; Ios_Click(sender, e); }
        }

        void View_Paint(object sender, PaintEventArgs e)
        {
            if (qrBmp == null) return;
            int avail = Math.Min(view.ClientSize.Width, view.ClientSize.Height) - Px(20);
            int scale = Math.Max(1, avail / qrBmp.Width);
            int w = qrBmp.Width * scale;
            int x = (view.ClientSize.Width - w) / 2, y = (view.ClientSize.Height - w) / 2;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.DrawImage(qrBmp, new Rectangle(x, y, w, w));
        }

        void View_Resize(object sender, EventArgs e) { view.Invalidate(); }
        void Prev_Click(object sender, EventArgs e) { Show(index - 1); }
        void Next_Click(object sender, EventArgs e) { Show(index + 1); }

        void Size_Changed(object sender, EventArgs e)
        {
            LastSize = sizeBox.SelectedIndex;
            Split();
            Show(0);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Left || keyData == Keys.PageUp) { Show(index - 1); return true; }
            if (keyData == Keys.Right || keyData == Keys.PageDown) { Show(index + 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (qrBmp != null) qrBmp.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ================= 상용구 (자주 쓰는 문구, 서식 포함) =================

    public class Snippet
    {
        public string Name = "";
        public int Key = 0;        // Alt+1~9, 0 이면 없음
        public string Rtf = "";    // 라이트 기준 색으로 저장
        public override string ToString() { return Name; }
    }

    public static class Snippets
    {
        public static List<Snippet> Items = new List<Snippet>();
        public static string FilePath;
        public static event EventHandler Changed;

        public static void Load()
        {
            Items.Clear();
            try
            {
                if (FilePath == null || !File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 3) continue;
                    Snippet s = new Snippet();
                    s.Name = p[0];
                    int.TryParse(p[1], out s.Key);
                    try { s.Rtf = Encoding.UTF8.GetString(Convert.FromBase64String(p[2])); } catch { continue; }
                    Items.Add(s);
                }
            }
            catch { }
        }

        public static void Save()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Snippet s in Items)
                sb.Append(s.Name.Replace("\t", " ")).Append('\t').Append(s.Key).Append('\t')
                  .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Rtf))).Append("\r\n");
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        public static Snippet ByKey(int key)
        {
            foreach (Snippet s in Items) if (s.Key == key) return s;
            return null;
        }

        public static int FreeKey()
        {
            for (int k = 1; k <= 9; k++) if (ByKey(k) == null) return k;
            return 0;
        }

        // 다른 상용구가 쓰던 번호면 빼앗기
        public static void SetKey(Snippet s, int key)
        {
            if (key > 0) foreach (Snippet o in Items) if (o != s && o.Key == key) o.Key = 0;
            s.Key = key;
        }

        public static string Label(Snippet s)
        {
            return s.Name + (s.Key > 0 ? "\t(Alt+" + s.Key + ")" : "");
        }

        // RTF 끝의 줄바꿈 하나 빼기 (넣을 때 빈 줄이 생기지 않게)
        public static string TrimLastPar(string rtf)
        {
            int end = rtf.LastIndexOf('}');
            if (end < 0) return rtf;
            int p = rtf.LastIndexOf("\\par", end);
            if (p < 0) return rtf;
            if (rtf.Substring(p + 4, end - p - 4).Trim().Length != 0) return rtf;
            return rtf.Substring(0, p) + rtf.Substring(p + 4);
        }

        // 편집기에서 선택한 부분을 상용구 RTF 로 (라이트 기준 색)
        public static string FromSelection(NoteEditor ed)
        {
            string rtf = ed.SelectedRtf;
            if (!ed.SelectedText.EndsWith("\n")) rtf = TrimLastPar(rtf);
            if (ColorMap.Dark) rtf = ColorMap.MapColorTable(rtf, false);
            return rtf;
        }

        public static void Insert(NoteEditor ed, Snippet s)
        {
            if (ed == null || s == null || ed.ReadOnly) return;
            string rtf = ColorMap.Dark ? ColorMap.MapColorTable(s.Rtf, true) : s.Rtf;
            int s0 = ed.SelectionStart;
            int rest = ed.TextLength - ed.SelectionLength;
            try { ed.SelectedRtf = rtf; }
            catch { return; }
            int s1 = s0 + Math.Max(0, ed.TextLength - rest);   // 넣은 글 바로 뒤
            ed.Select(s1, 0);
            if (ColorMap.Dark && s1 > s0) ed.FixTextColors(s0, s1, true, s1);
            ed.MarkDirty();
            ed.Focus();
        }

        public static void SaveSelection(NoteEditor ed)
        {
            Form owner = ed.FindForm();
            if (ed.SelectionLength == 0)
            {
                MessageBox.Show(owner, "상용구로 저장할 글을 먼저 선택(블록 설정)하세요.", "AHNNOTE");
                return;
            }
            string first = ed.SelectedText.Trim().Replace("\n", " ");
            if (first.Length > 20) first = first.Substring(0, 20);
            string name = InputDialog.Ask(owner, "상용구로 저장", "상용구 이름:", first);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            Snippet sn = new Snippet();
            sn.Name = name;
            sn.Rtf = FromSelection(ed);
            sn.Key = FreeKey();
            Items.Add(sn);
            Save();
            MessageBox.Show(owner, "'" + name + "' 상용구를 저장했습니다." + (sn.Key > 0 ? "\nAlt+" + sn.Key + " 로 넣을 수 있습니다." : "\n(Alt+1~9 가 모두 쓰이고 있어 단축키는 없습니다)"), "AHNNOTE");
        }

        public static void ShowManager(Form owner, Font baseFont, float scale)
        {
            using (SnippetForm f = new SnippetForm(baseFont, scale))
            {
                f.TopMost = owner.TopMost;
                f.ShowDialog(owner);
            }
        }
    }

    // 우클릭 메뉴 / 도구 모음에 상용구 항목 넣기
    public class SnippetMenu
    {
        NoteEditor ed;
        ToolStripMenuItem insertMenu;
        Form owner;
        Font baseFont;
        float scale;

        public SnippetMenu(NoteEditor editor, Font font, float s)
        {
            ed = editor;
            baseFont = font;
            scale = s;
        }

        // 우클릭 메뉴용
        public void Attach(ContextMenuStrip m)
        {
            insertMenu = new ToolStripMenuItem("상용구 넣기");
            ToolStripMenuItem save = new ToolStripMenuItem("선택한 글을 상용구로 저장", null, new EventHandler(Save_Click));
            int at = 3;   // 글자색, 형광펜, 구분선 다음
            if (m.Items.Count < at) at = m.Items.Count;
            m.Items.Insert(at, new ToolStripSeparator());
            m.Items.Insert(at, save);
            m.Items.Insert(at, insertMenu);
            m.Opening += new System.ComponentModel.CancelEventHandler(Menu_Opening);
        }

        // 도구 모음 버튼용
        public void AttachButton(ToolStripDropDownButton b)
        {
            b.DropDownOpening += new EventHandler(Button_Opening);
            b.DropDownItems.Add("(비어 있음)");
        }

        void Fill(ToolStripItemCollection items, bool withTools)
        {
            items.Clear();
            foreach (Snippet s in Snippets.Items)
            {
                ToolStripMenuItem mi = new ToolStripMenuItem(s.Name);
                if (s.Key > 0) mi.ShortcutKeyDisplayString = "Alt+" + s.Key;
                mi.Tag = s;
                mi.Click += new EventHandler(Insert_Click);
                mi.Enabled = !ed.ReadOnly;
                items.Add(mi);
            }
            if (Snippets.Items.Count == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem("저장된 상용구가 없습니다");
                empty.Enabled = false;
                items.Add(empty);
            }
            if (withTools)
            {
                items.Add(new ToolStripSeparator());
                items.Add(new ToolStripMenuItem("선택한 글을 상용구로 저장", null, new EventHandler(Save_Click)));
            }
            items.Add(new ToolStripMenuItem("상용구 관리...", null, new EventHandler(Manage_Click)));
        }

        void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Fill(insertMenu.DropDownItems, false);
        }

        void Button_Opening(object sender, EventArgs e)
        {
            Fill(((ToolStripDropDownButton)sender).DropDownItems, true);
        }

        void Insert_Click(object sender, EventArgs e)
        {
            Snippets.Insert(ed, (Snippet)((ToolStripItem)sender).Tag);
        }

        void Save_Click(object sender, EventArgs e) { Snippets.SaveSelection(ed); }

        void Manage_Click(object sender, EventArgs e)
        {
            owner = ed.FindForm();
            Snippets.ShowManager(owner, baseFont, scale);
        }
    }

    // ================= 상용구 관리 창 =================

    public class SnippetForm : Form
    {
        ListBox list;
        TextBox nameBox;
        ComboBox keyBox;
        NoteEditor content;
        Button newBtn, saveBtn, delBtn, upBtn, downBtn;
        Snippet editing;
        float S;

        int Px(int v) { return (int)(v * S); }

        public SnippetForm(Font baseFont, float scale)
        {
            S = scale;
            Text = "상용구 관리";
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Px(700), Px(440));
            MinimumSize = new Size(Px(560), Px(360));

            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = Px(220);
            left.Padding = new Padding(Px(10), Px(10), Px(6), Px(10));
            list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            list.SelectedIndexChanged += new EventHandler(List_Selected);
            FlowLayoutPanel lb = new FlowLayoutPanel();
            lb.Dock = DockStyle.Bottom;
            lb.Height = Px(38);
            lb.Padding = new Padding(0, Px(4), 0, 0);
            newBtn = SmallButton("새로 만들기", 90);
            upBtn = SmallButton("▲", 36);
            downBtn = SmallButton("▼", 36);
            newBtn.Click += new EventHandler(New_Click);
            upBtn.Click += new EventHandler(Up_Click);
            downBtn.Click += new EventHandler(Down_Click);
            lb.Controls.Add(newBtn);
            lb.Controls.Add(upBtn);
            lb.Controls.Add(downBtn);
            left.Controls.Add(list);
            left.Controls.Add(lb);

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(Px(6), Px(10), Px(10), Px(10));
            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.Height = Px(34);
            top.ColumnCount = 4;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(44)));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(56)));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(96)));
            Label nl = new Label();
            nl.Text = "이름";
            nl.TextAlign = ContentAlignment.MiddleLeft;
            nl.Dock = DockStyle.Fill;
            nameBox = new TextBox();
            nameBox.Dock = DockStyle.Fill;
            Label kl = new Label();
            kl.Text = "단축키";
            kl.TextAlign = ContentAlignment.MiddleRight;
            kl.Dock = DockStyle.Fill;
            keyBox = new ComboBox();
            keyBox.DropDownStyle = ComboBoxStyle.DropDownList;
            keyBox.Dock = DockStyle.Fill;
            keyBox.Items.Add("없음");
            for (int k = 1; k <= 9; k++) keyBox.Items.Add("Alt+" + k);
            top.Controls.Add(nl, 0, 0);
            top.Controls.Add(nameBox, 1, 0);
            top.Controls.Add(kl, 2, 0);
            top.Controls.Add(keyBox, 3, 0);

            Label hint = new Label();
            hint.Dock = DockStyle.Top;
            hint.Height = Px(26);
            hint.TextAlign = ContentAlignment.BottomLeft;
            hint.ForeColor = Color.Gray;
            hint.Text = "내용 (Ctrl+B, Ctrl+Shift+C 등 서식 단축키를 그대로 쓸 수 있습니다)";

            Panel box = new Panel();
            box.Dock = DockStyle.Fill;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Padding = new Padding(Px(6));
            content = new NoteEditor(baseFont, scale);
            content.Dock = DockStyle.Fill;
            box.Controls.Add(content);

            FlowLayoutPanel rb = new FlowLayoutPanel();
            rb.Dock = DockStyle.Bottom;
            rb.Height = Px(40);
            rb.FlowDirection = FlowDirection.RightToLeft;
            rb.Padding = new Padding(0, Px(6), 0, 0);
            Button close = SmallButton("닫기", 80);
            close.DialogResult = DialogResult.Cancel;
            saveBtn = SmallButton("저장", 80);
            delBtn = SmallButton("삭제", 80);
            saveBtn.Click += new EventHandler(Save_Click);
            delBtn.Click += new EventHandler(Del_Click);
            rb.Controls.Add(close);
            rb.Controls.Add(saveBtn);
            rb.Controls.Add(delBtn);
            CancelButton = close;

            right.Controls.Add(box);
            right.Controls.Add(hint);
            right.Controls.Add(top);
            right.Controls.Add(rb);

            Controls.Add(right);
            Controls.Add(left);
            Skin.Apply(this);
            content.BackColor = ColorMap.Disp(Color.White);
            box.BackColor = content.BackColor;

            FillList(null);
        }

        // 창이 만들어진 뒤에 불러와야 서식이 그대로 보임
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Snippets.Items.Count > 0) list.SelectedIndex = 0;
            else StartNew();
        }

        Button SmallButton(string text, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(Px(w), Px(28));
            b.Margin = new Padding(Px(2), 0, Px(2), 0);
            return b;
        }

        void FillList(Snippet select)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Snippet s in Snippets.Items) list.Items.Add(new SnippetRow(s));
            list.EndUpdate();
            if (select != null)
                for (int i = 0; i < list.Items.Count; i++)
                    if (((SnippetRow)list.Items[i]).S == select) { list.SelectedIndex = i; break; }
        }

        class SnippetRow
        {
            public Snippet S;
            public SnippetRow(Snippet s) { S = s; }
            public override string ToString() { return S.Name + (S.Key > 0 ? "   (Alt+" + S.Key + ")" : ""); }
        }

        void ShowSnippet(Snippet s)
        {
            editing = s;
            nameBox.Text = s.Name;
            keyBox.SelectedIndex = Math.Max(0, Math.Min(9, s.Key));
            content.Loading = true;
            content.Clear();
            try
            {
                if (s.Rtf.Length > 0) content.Rtf = ColorMap.Dark ? ColorMap.MapColorTable(s.Rtf, true) : s.Rtf;
            }
            catch { }
            if (ColorMap.Dark) content.FixTextColors(0, content.TextLength, false, 0);
            content.Select(0, 0);
            content.ClearUndo();
            content.Loading = false;
            content.Dirty = false;
            delBtn.Enabled = Snippets.Items.Contains(s);
        }

        void StartNew()
        {
            list.SelectedIndex = -1;
            Snippet s = new Snippet();
            s.Name = "새 상용구";
            s.Key = Snippets.FreeKey();
            ShowSnippet(s);
            nameBox.Focus();
            nameBox.SelectAll();
        }

        void List_Selected(object sender, EventArgs e)
        {
            if (list.SelectedIndex < 0) return;
            ShowSnippet(((SnippetRow)list.SelectedItem).S);
        }

        void New_Click(object sender, EventArgs e) { StartNew(); }

        void Save_Click(object sender, EventArgs e)
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0) { MessageBox.Show(this, "이름을 입력하세요.", "AHNNOTE"); nameBox.Focus(); return; }
            if (content.TextLength == 0) { MessageBox.Show(this, "내용을 입력하세요.", "AHNNOTE"); content.Focus(); return; }
            editing.Name = name;
            string rtf = Snippets.TrimLastPar(content.Rtf);
            if (ColorMap.Dark) rtf = ColorMap.MapColorTable(rtf, false);
            editing.Rtf = rtf;
            if (!Snippets.Items.Contains(editing)) Snippets.Items.Add(editing);
            Snippets.SetKey(editing, keyBox.SelectedIndex);
            Snippets.Save();
            content.Dirty = false;
            FillList(editing);
            delBtn.Enabled = true;
        }

        void Del_Click(object sender, EventArgs e)
        {
            if (editing == null || !Snippets.Items.Contains(editing)) return;
            if (MessageBox.Show(this, "'" + editing.Name + "' 상용구를 삭제할까요?", "AHNNOTE", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int i = Snippets.Items.IndexOf(editing);
            Snippets.Items.Remove(editing);
            Snippets.Save();
            FillList(null);
            if (Snippets.Items.Count > 0) list.SelectedIndex = Math.Min(i, Snippets.Items.Count - 1);
            else StartNew();
        }

        void MoveItem(int d)
        {
            int i = Snippets.Items.IndexOf(editing);
            int j = i + d;
            if (i < 0 || j < 0 || j >= Snippets.Items.Count) return;
            Snippet t = Snippets.Items[i];
            Snippets.Items[i] = Snippets.Items[j];
            Snippets.Items[j] = t;
            Snippets.Save();
            FillList(editing);
        }

        void Up_Click(object sender, EventArgs e) { MoveItem(-1); }
        void Down_Click(object sender, EventArgs e) { MoveItem(1); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (content.Dirty && editing != null &&
                MessageBox.Show(this, "고친 내용을 저장할까요?", "AHNNOTE", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Save_Click(this, EventArgs.Empty);
            base.OnFormClosing(e);
        }
    }

    // ================= 찾기 / 바꾸기 창 =================

    public class FindForm : Form
    {
        RichTextBox ed;
        TextBox findBox, replBox;
        CheckBox caseChk;
        Label replLabel, msgLabel;
        Button btnNext, btnPrev, btnRepl, btnAll, btnClose;
        float S;

        public string FindText { get { return findBox.Text; } }

        int Px(int v) { return (int)(v * S); }

        public FindForm(Form owner, RichTextBox editor, float scale)
        {
            ed = editor;
            S = scale;
            Owner = owner;
            Text = "찾기";
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Px(420), Px(150));

            Label l1 = new Label();
            l1.Text = "찾을 내용";
            l1.SetBounds(Px(10), Px(14), Px(70), Px(20));
            findBox = new TextBox();
            findBox.SetBounds(Px(80), Px(10), Px(220), Px(24));

            replLabel = new Label();
            replLabel.Text = "바꿀 내용";
            replLabel.SetBounds(Px(10), Px(46), Px(70), Px(20));
            replBox = new TextBox();
            replBox.SetBounds(Px(80), Px(42), Px(220), Px(24));

            caseChk = new CheckBox();
            caseChk.Text = "대/소문자 구분";
            caseChk.SetBounds(Px(80), Px(74), Px(200), Px(22));

            msgLabel = new Label();
            msgLabel.ForeColor = Color.FromArgb(224, 62, 62);
            msgLabel.SetBounds(Px(10), Px(118), Px(290), Px(20));

            btnNext = MakeButton("다음 찾기", 8);
            btnPrev = MakeButton("이전 찾기", 38);
            btnRepl = MakeButton("바꾸기", 68);
            btnAll = MakeButton("모두 바꾸기", 98);
            btnClose = MakeButton("닫기", 128);
            btnClose.Visible = false;

            btnNext.Click += new EventHandler(Next_Click);
            btnPrev.Click += new EventHandler(Prev_Click);
            btnRepl.Click += new EventHandler(Repl_Click);
            btnAll.Click += new EventHandler(All_Click);
            btnClose.Click += new EventHandler(Close_Click);

            Controls.Add(l1); Controls.Add(findBox);
            Controls.Add(replLabel); Controls.Add(replBox);
            Controls.Add(caseChk); Controls.Add(msgLabel);
            AcceptButton = btnNext;
            CancelButton = btnClose;
        }

        Button MakeButton(string text, int y)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(Px(310), Px(y), Px(100), Px(26));
            Controls.Add(b);
            return b;
        }

        public void SetFindText(string s) { findBox.Text = s; }

        public void Open(bool replace)
        {
            Text = replace ? "바꾸기" : "찾기";
            replLabel.Visible = replace;
            replBox.Visible = replace;
            btnRepl.Visible = replace;
            btnAll.Visible = replace;
            msgLabel.Text = "";
            if (!Visible)
            {
                Rectangle r = Owner.Bounds;
                Location = new Point(r.Right - Width - Px(30), r.Top + Px(110));
                Show();
            }
            Activate();
            findBox.Focus();
            findBox.SelectAll();
        }

        RichTextBoxFinds Opt()
        {
            return caseChk.Checked ? RichTextBoxFinds.MatchCase : RichTextBoxFinds.None;
        }

        public bool FindNext(bool forward)
        {
            string t = findBox.Text;
            msgLabel.Text = "";
            msgLabel.ForeColor = Color.FromArgb(224, 62, 62);
            if (t.Length == 0) return false;
            int idx;
            if (forward)
            {
                int start = ed.SelectionStart + ed.SelectionLength;
                idx = start < ed.TextLength ? ed.Find(t, start, Opt()) : -1;
                if (idx < 0) idx = ed.Find(t, 0, Opt());
            }
            else
            {
                RichTextBoxFinds o = Opt() | RichTextBoxFinds.Reverse;
                idx = ed.SelectionStart > 0 ? ed.Find(t, 0, ed.SelectionStart, o) : -1;
                if (idx < 0) idx = ed.Find(t, 0, ed.TextLength, o);
            }
            if (idx < 0)
            {
                msgLabel.Text = "'" + t + "' 을(를) 찾을 수 없습니다.";
                return false;
            }
            ed.ScrollToCaret();
            return true;
        }

        void Next_Click(object sender, EventArgs e) { FindNext(true); }
        void Prev_Click(object sender, EventArgs e) { FindNext(false); }

        void Repl_Click(object sender, EventArgs e)
        {
            string t = findBox.Text;
            if (t.Length == 0) return;
            StringComparison sc = caseChk.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (ed.SelectionLength > 0 && string.Equals(ed.SelectedText, t, sc))
                ed.SelectedText = replBox.Text;
            FindNext(true);
        }

        void All_Click(object sender, EventArgs e)
        {
            string t = findBox.Text;
            if (t.Length == 0) return;
            string r = replBox.Text;
            int count = 0;
            int pos = 0;
            while (pos < ed.TextLength)
            {
                int idx = ed.Find(t, pos, Opt());
                if (idx < 0) break;
                ed.SelectedText = r;
                count++;
                pos = idx + r.Length;
            }
            msgLabel.ForeColor = Color.FromArgb(15, 123, 108);
            msgLabel.Text = count + "개를 바꿨습니다.";
        }

        void Close_Click(object sender, EventArgs e) { Hide(); if (Owner != null) Owner.Activate(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (Owner != null) Owner.Activate();
                return;
            }
            base.OnFormClosing(e);
        }

    }

    // ================= 이름 입력 창 =================

    // 이름 입력창: 뜨자마자 입력칸에 커서가 들어가게 함
    public class InputForm : Form
    {
        public TextBox Box;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            if (Box != null)
            {
                Box.Focus();
                Box.SelectAll();
            }
        }
    }

    public static class InputDialog
    {
        public static string Ask(Form owner, string title, string prompt, string value)
        {
            float s = 1f;
            using (Graphics g = owner.CreateGraphics()) { s = g.DpiX / 96f; }
            using (InputForm f = new InputForm())
            {
                f.Text = title;
                f.Font = new Font("Malgun Gothic", 9f);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size((int)(340 * s), (int)(120 * s));

                Label l = new Label();
                l.Text = prompt;
                l.SetBounds((int)(12 * s), (int)(12 * s), (int)(310 * s), (int)(20 * s));
                TextBox tb = new TextBox();
                tb.Text = value;
                tb.SetBounds((int)(12 * s), (int)(38 * s), (int)(316 * s), (int)(24 * s));
                Button ok = new Button();
                ok.Text = "확인";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds((int)(168 * s), (int)(78 * s), (int)(76 * s), (int)(28 * s));
                Button cancel = new Button();
                cancel.Text = "취소";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds((int)(252 * s), (int)(78 * s), (int)(76 * s), (int)(28 * s));

                f.Controls.Add(l);
                f.Controls.Add(tb);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Box = tb;
                f.ActiveControl = tb;
                tb.SelectAll();
                Skin.Apply(f);
                f.TopMost = owner.TopMost;   // 항상 위일 때 입력창이 뒤로 숨지 않게

                if (f.ShowDialog(owner) == DialogResult.OK) return tb.Text;
                return null;
            }
        }
    }

    // ================= 앱 아이콘 (파란 A) =================

    public static class AppIcon
    {
        static readonly Color ACCENT = Color.FromArgb(0, 103, 192);

        public static Bitmap DrawA(int size)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int r = Math.Max(2, size / 5);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(size - r * 2 - 1, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(size - r * 2 - 1, size - r * 2 - 1, r * 2, r * 2, 0, 90);
                    path.AddArc(0, size - r * 2 - 1, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    using (SolidBrush br = new SolidBrush(ACCENT)) g.FillPath(br, path);
                }
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("A", f, Brushes.White, new RectangleF(0, size * 0.04f, size, size), sf);
                }
            }
            return b;
        }

        // 여러 크기가 들어간 .ico 파일 만들기 (256 은 PNG, 나머지는 BMP)
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 256 };
            List<byte[]> imgs = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (Bitmap b = DrawA(s))
                    imgs.Add(s >= 256 ? PngBytes(b) : DibBytes(b));
            }
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(imgs[i].Length);
                    w.Write(offset);
                    offset += imgs[i].Length;
                }
                foreach (byte[] img in imgs) w.Write(img);
            }
        }

        static byte[] PngBytes(Bitmap b)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static byte[] DibBytes(Bitmap b)
        {
            int s = b.Width;
            int maskStride = ((s + 31) / 32) * 4;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40);
                w.Write(s);
                w.Write(s * 2);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(0);
                w.Write(s * s * 4 + maskStride * s);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                {
                    for (int x = 0; x < s; x++)
                    {
                        Color c = b.GetPixel(x, y);
                        w.Write(c.B);
                        w.Write(c.G);
                        w.Write(c.R);
                        w.Write(c.A);
                    }
                }
                w.Write(new byte[maskStride * s]);
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    // ================= 이스터에그: 두더지 잡기 =================

    public class MoleGame : Form
    {
        // 화면 (도트 150 x 210 을 2배 이상으로 키워서 그림)
        const int W = 150;
        const int H = 210;

        // 단계 설정: 1단계 아주 쉬움, 2단계 중간, 3단계 어려움
        static readonly int[] STAGE_COUNT = { 6, 8, 10 };        // 두더지 수
        static readonly int[] STAGE_SPAWN = { 1300, 750, 480 };  // 다음 두더지까지 (ms)
        static readonly int[] STAGE_SHOW = { 1200, 1000, 700 };  // 보이는 시간 (ms)
        static readonly string[] STAGE_NAME = { "EASY", "NORMAL", "HARD" };
        const int UP_MS = 120;
        const int HIT_MS = 250;
        const int INTRO_MS = 1200;

        const int ST_TITLE = 0;
        const int ST_INTRO = 1;
        const int ST_PLAY = 2;
        const int ST_RESULT = 3;

        static readonly string[] MOLE = {
            "..BBBBBBBB..", ".BBBBBBBBBB.", "BBBKBBBBKBBB", "BBBKBBBBKBBB",
            "BBBBBNNBBBBB", "BBBBNNNNBBBB", "BBBBBBBBBBBB", ".BBBBBBBBBB." };
        static readonly string[] MOLE_HIT = {
            "..BBBBBBBB..", ".BBBBBBBBBB.", "BBKBKBBKBKBB", "BBBKBBBBKBBB",
            "BBBBBNNBBBBB", "BBBBNNNNBBBB", "BBBBBBBBBBBB", ".BBBBBBBBBB." };
        static readonly string[] CROWN = { "Y.Y.Y", "YYYYY", "YYYYY" };
        static readonly string[] FACE = {
            "....KKKKKKKK....", "..KKBBBBBBBBKK..", ".KBBBBBBBBBBBBK.", ".KBBBBBBBBBBBBK.",
            "KBBBBBBBBBBBBBBK", "KBBBKKBBBBKKBBBK", "KBBBKKBBBBKKBBBK", "KBBBBBBBBBBBBBBK",
            "KBBBBLLLLLLBBBBK", "KBBBLLLNNLLLBBBK", "KBBBLLNNNNLLBBBK", "KBBBBLLWWLLBBBBK",
            ".KBBBBBBBBBBBBK.", ".KBBBBBBBBBBBBK.", "..KKBBBBBBBBKK..", "....KKKKKKKK...." };

        static readonly Color C_GRASS = Color.FromArgb(95, 160, 70);
        static readonly Color C_GRASS2 = Color.FromArgb(86, 149, 61);
        static readonly Color C_TUFT = Color.FromArgb(120, 189, 90);
        static readonly Color C_DIRT = Color.FromArgb(122, 75, 36);
        static readonly Color C_DIRTD = Color.FromArgb(63, 37, 18);
        static readonly Color C_DIRTL = Color.FromArgb(165, 108, 52);
        static readonly Color C_MOLE = Color.FromArgb(155, 106, 59);
        static readonly Color C_SNOUT = Color.FromArgb(201, 154, 104);
        static readonly Color C_NOSE = Color.FromArgb(240, 138, 160);
        static readonly Color C_INK = Color.FromArgb(28, 20, 16);
        static readonly Color C_HUD = Color.FromArgb(58, 38, 22);
        static readonly Color C_GOLD = Color.FromArgb(255, 210, 63);
        static readonly Color C_RED = Color.FromArgb(228, 87, 46);
        static readonly Color C_GREY = Color.FromArgb(107, 90, 73);

        class Mole
        {
            public int Cell;
            public long T0;
            public int Show;
            public bool Hit;
            public long HitAt;
            public float P0;
        }

        public int Best;

        int scale;
        Bitmap buf;
        Timer timer;
        System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        Random rnd = new Random();
        Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        Font fSmall, fMid, fBig;

        int state = ST_TITLE;
        long stateAt = 0;
        int stage = 0;
        int spawned = 0;
        int lastCell = -1;
        long nextSpawn = 0;
        bool newBest = false;
        List<Mole> moles = new List<Mole>();
        List<int> results = new List<int>();
        int[] stageHits = new int[3];
        List<float[]> confetti = new List<float[]>();
        List<long[]> pops = new List<long[]>();

        public MoleGame(int best, float dpiScale)
        {
            Best = best;
            scale = Math.Max(2, (int)Math.Round(2 * dpiScale));
            Text = "두더지 잡기";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            ClientSize = new Size(W * scale, H * scale);
            Icon = MakeFaceIcon();
            BackColor = Color.Black;

            fSmall = new Font("Consolas", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
            fMid = new Font("Consolas", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
            fBig = new Font("Consolas", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            buf = new Bitmap(W, H);

            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += new EventHandler(Timer_Tick);
            clock.Start();
            timer.Start();
            Skin.TitleBar(this, Skin.Dark);
        }

        Icon MakeFaceIcon()
        {
            try
            {
                Bitmap b = new Bitmap(32, 32);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    DrawSprite(g, FACE, 0, 0, 2, false);
                }
                return Icon.FromHandle(b.GetHicon());
            }
            catch { return null; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            timer.Dispose();
            buf.Dispose();
            foreach (SolidBrush b in brushes.Values) b.Dispose();
            brushes.Clear();
            fSmall.Dispose();
            fMid.Dispose();
            fBig.Dispose();
            base.OnFormClosed(e);
        }

        // ---------- 입력 ----------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            int cell = -1;
            switch (e.KeyCode)
            {
                case Keys.NumPad7: cell = 0; break;
                case Keys.NumPad8: cell = 1; break;
                case Keys.NumPad9: cell = 2; break;
                case Keys.NumPad4: cell = 3; break;
                case Keys.NumPad5: cell = 4; break;
                case Keys.NumPad6: cell = 5; break;
                case Keys.NumPad1: cell = 6; break;
                case Keys.NumPad2: cell = 7; break;
                case Keys.NumPad3: cell = 8; break;
            }
            if (cell >= 0 && state == ST_PLAY) TryHit(cell, clock.ElapsedMilliseconds);
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            if (e.Button != MouseButtons.Left) return;
            if (state == ST_TITLE) { StartGame(now); return; }
            if (state == ST_RESULT) { if (now - stateAt > 800) StartGame(now); return; }
            if (state != ST_PLAY) return;
            int x = e.X / scale, y = e.Y / scale;
            for (int i = 0; i < 9; i++)
            {
                Point p = CellPos(i);
                if (x >= p.X && x <= p.X + 40 && y >= p.Y - 8 && y <= p.Y + 38) { TryHit(i, now); return; }
            }
        }

        // ---------- 게임 진행 ----------

        void StartGame(long now)
        {
            moles.Clear();
            results.Clear();
            pops.Clear();
            confetti.Clear();
            stageHits = new int[3];
            stage = 0;
            spawned = 0;
            lastCell = -1;
            newBest = false;
            state = ST_INTRO;
            stateAt = now;
        }

        int Total()
        {
            int n = 0;
            foreach (int c in STAGE_COUNT) n += c;
            return n;
        }

        int Hits()
        {
            int n = 0;
            foreach (int r in results) if (r == 1) n++;
            return n;
        }

        Point CellPos(int i) { return new Point(10 + (i % 3) * 45, 62 + (i / 3) * 45); }

        float Prog(Mole m, long now)
        {
            if (m.Hit) return Math.Max(0f, m.P0 * (1f - (now - m.HitAt) / (float)HIT_MS));
            long age = now - m.T0;
            float p;
            if (age < UP_MS) p = age / (float)UP_MS;
            else if (age > m.Show - UP_MS) p = (m.Show - age) / (float)UP_MS;
            else p = 1f;
            if (p < 0f) p = 0f;
            if (p > 1f) p = 1f;
            return p;
        }

        void TryHit(int cell, long now)
        {
            foreach (Mole m in moles)
            {
                if (m.Hit || m.Cell != cell) continue;
                float p = Prog(m, now);
                if (p < 0.3f) continue;
                m.Hit = true;
                m.HitAt = now;
                m.P0 = p;
                results.Add(1);
                stageHits[stage]++;
                Point c = CellPos(cell);
                pops.Add(new long[] { c.X + 20, c.Y - 6, now });
                return;
            }
        }

        int FreeCell()
        {
            List<int> free = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                bool used = false;
                foreach (Mole m in moles) if (m.Cell == i) used = true;
                if (!used && i != lastCell) free.Add(i);
            }
            if (free.Count == 0) return -1;
            return free[rnd.Next(free.Count)];
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            Step(clock.ElapsedMilliseconds);
            Invalidate();
        }

        void Step(long now)
        {
            if (state == ST_INTRO && now - stateAt >= INTRO_MS)
            {
                state = ST_PLAY;
                stateAt = now;
                nextSpawn = now;
            }
            else if (state == ST_PLAY)
            {
                for (int i = moles.Count - 1; i >= 0; i--)
                {
                    Mole m = moles[i];
                    if (m.Hit) { if (now - m.HitAt >= HIT_MS) moles.RemoveAt(i); }
                    else if (now - m.T0 >= m.Show) { results.Add(0); moles.RemoveAt(i); }
                }
                if (spawned < STAGE_COUNT[stage] && now >= nextSpawn)
                {
                    int c = FreeCell();
                    if (c >= 0)
                    {
                        Mole m = new Mole();
                        m.Cell = c;
                        m.T0 = now;
                        m.Show = STAGE_SHOW[stage];
                        moles.Add(m);
                        lastCell = c;
                        spawned++;
                        nextSpawn = now + STAGE_SPAWN[stage];
                    }
                }
                if (spawned >= STAGE_COUNT[stage] && moles.Count == 0)
                {
                    stage++;
                    spawned = 0;
                    if (stage >= STAGE_COUNT.Length) Finish(now);
                    else { state = ST_INTRO; stateAt = now; }
                }
            }
        }

        void Finish(long now)
        {
            state = ST_RESULT;
            stateAt = now;
            stage = STAGE_COUNT.Length - 1;
            pops.Clear();
            int h = Hits();
            if (h > Best) { Best = h; newBest = true; }
            if (h == Total())
            {
                for (int i = 0; i < 70; i++)
                    confetti.Add(new float[] { (float)rnd.NextDouble() * W, -(float)rnd.NextDouble() * H, 0.6f + (float)rnd.NextDouble() * 1.2f, rnd.Next(6) });
            }
        }

        // ---------- 그리기 ----------

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            using (Graphics g = Graphics.FromImage(buf))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                DrawGame(g, now);
            }
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(buf, new Rectangle(0, 0, W * scale, H * scale));
        }

        SolidBrush Br(Color c)
        {
            SolidBrush b;
            int k = c.ToArgb();
            if (!brushes.TryGetValue(k, out b)) { b = new SolidBrush(c); brushes[k] = b; }
            return b;
        }

        void Rect(Graphics g, Color c, int x, int y, int w, int h) { g.FillRectangle(Br(c), x, y, w, h); }

        void Ell(Graphics g, Color c, int cx, int cy, int rx, int ry)
        {
            for (int y = -ry; y <= ry; y++)
            {
                int w = (int)Math.Round(rx * Math.Sqrt(1.0 - (y * y) / (ry * ry + 0.5)));
                g.FillRectangle(Br(c), cx - w, cy + y, w * 2, 1);
            }
        }

        Color SpriteColor(char ch)
        {
            switch (ch)
            {
                case 'B': return C_MOLE;
                case 'K': return C_INK;
                case 'N': return C_NOSE;
                case 'L': return C_SNOUT;
                case 'W': return Color.White;
                case 'Y': return C_GOLD;
            }
            return Color.Transparent;
        }

        void DrawSprite(Graphics g, string[] rows, int x, int y, int s, bool outline)
        {
            int r, c;
            if (outline)
            {
                int[] dx = { 1, -1, 0, 0 };
                int[] dy = { 0, 0, 1, -1 };
                for (int i = 0; i < 4; i++)
                    for (r = 0; r < rows.Length; r++)
                        for (c = 0; c < rows[r].Length; c++)
                            if (rows[r][c] != '.') Rect(g, C_INK, x + c * s + dx[i], y + r * s + dy[i], s, s);
            }
            for (r = 0; r < rows.Length; r++)
                for (c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] != '.') Rect(g, SpriteColor(rows[r][c]), x + c * s, y + r * s, s, s);
        }

        float TextWidth(Graphics g, string s, Font f)
        {
            return g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        void Say(Graphics g, string s, Font f, Color c, float x, float y, bool center)
        {
            if (center) x -= TextWidth(g, s, f) / 2f;
            g.DrawString(s, f, Br(C_INK), x + 1, y + 1, StringFormat.GenericTypographic);
            g.DrawString(s, f, Br(c), x, y, StringFormat.GenericTypographic);
        }

        static Color Hue(double h)
        {
            h = h % 360.0;
            if (h < 0) h += 360.0;
            double x = 1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0);
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = 1; g = x; }
            else if (h < 120) { r = x; g = 1; }
            else if (h < 180) { g = 1; b = x; }
            else if (h < 240) { g = x; b = 1; }
            else if (h < 300) { r = x; b = 1; }
            else { r = 1; b = x; }
            return Color.FromArgb((int)(120 + r * 135), (int)(120 + g * 135), (int)(120 + b * 135));
        }

        void DrawGame(Graphics g, long now)
        {
            // 풀밭
            Rect(g, C_GRASS, 0, 0, W, H);
            for (int y = 44; y < H; y += 10)
                for (int x = ((y / 10) % 2) * 10; x < W; x += 20)
                    Rect(g, C_GRASS2, x, y, 10, 10);
            int[] tx = { 8, 40, 84, 120, 140, 22, 110, 70 };
            int[] ty = { 200, 196, 203, 198, 204, 52, 54, 50 };
            for (int i = 0; i < tx.Length; i++)
            {
                Rect(g, C_TUFT, tx[i], ty[i], 1, 3);
                Rect(g, C_TUFT, tx[i] + 2, ty[i] + 1, 1, 2);
                Rect(g, C_TUFT, tx[i] + 4, ty[i], 1, 3);
            }

            DrawHud(g, now);
            for (int i = 0; i < 9; i++) DrawCell(g, i, now);

            // +1 표시
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                long age = now - pops[i][2];
                if (age > 450) { pops.RemoveAt(i); continue; }
                Say(g, "+1", fSmall, C_GOLD, pops[i][0], pops[i][1] - age / 40, true);
            }

            bool blink = (now / 400) % 2 == 0;
            if (state == ST_TITLE)
            {
                Overlay(g, 160);
                DrawSprite(g, MOLE, 57, 60, 3, true);
                Say(g, "WHACK", fBig, C_GOLD, 75, 98, true);
                Say(g, "A MOLE", fBig, Color.White, 75, 118, true);
                if (blink) Say(g, "CLICK TO START", fSmall, Color.White, 75, 166, true);
            }
            else if (state == ST_INTRO)
            {
                Overlay(g, 140);
                Say(g, "STAGE " + (stage + 1), fBig, Color.White, 75, 84, true);
                Say(g, STAGE_NAME[stage], fMid, stage == 2 ? C_RED : C_GOLD, 75, 108, true);
                Say(g, STAGE_COUNT[stage] + " MOLES", fSmall, Color.White, 75, 130, true);
            }
            else if (state == ST_RESULT)
            {
                if (Hits() == Total()) DrawPerfect(g, now, blink);
                else DrawEnding(g, now, blink);
            }
        }

        void DrawHud(Graphics g, long now)
        {
            Rect(g, C_HUD, 0, 0, W, 44);
            Rect(g, C_INK, 0, 44, W, 2);
            Say(g, "STAGE " + (state == ST_TITLE ? 1 : stage + 1), fSmall, Color.White, 6, 7, false);
            string hit = "HIT " + Hits().ToString("00") + "/" + Total();
            Say(g, hit, fSmall, C_GOLD, W - 6 - TextWidth(g, hit, fSmall), 7, false);
            int total = Total();
            for (int i = 0; i < total; i++)
            {
                Color c = C_GREY;
                if (i < results.Count) c = results[i] == 1 ? C_GOLD : C_RED;
                Rect(g, c, 3 + i * 6, 26, 5, 8);
            }
        }

        void DrawCell(Graphics g, int i, long now)
        {
            Point p = CellPos(i);
            int x = p.X, y = p.Y;
            Ell(g, C_DIRTD, x + 20, y + 31, 18, 6);
            if (state == ST_PLAY)
            {
                foreach (Mole m in moles)
                {
                    if (m.Cell != i) continue;
                    float mp = Prog(m, now);
                    if (mp <= 0f) continue;
                    GraphicsState gs = g.Save();
                    g.SetClip(new Rectangle(x - 2, y - 12, 44, 43));
                    DrawSprite(g, m.Hit ? MOLE_HIT : MOLE, x + 2, (int)Math.Round(y + 31 - 24 * mp), 3, true);
                    g.Restore(gs);
                }
            }
            GraphicsState gs2 = g.Save();
            g.SetClip(new Rectangle(x - 2, y + 31, 44, 12));
            Ell(g, C_DIRT, x + 20, y + 31, 19, 6);
            Ell(g, C_DIRTD, x + 20, y + 31, 15, 4);
            g.Restore(gs2);
            Rect(g, C_DIRTL, x + 3, y + 36, 3, 1);
            Rect(g, C_DIRTL, x + 33, y + 36, 3, 1);
            Rect(g, C_DIRTL, x + 12, y + 38, 6, 1);
        }

        void Overlay(Graphics g, int alpha)
        {
            Rect(g, Color.FromArgb(alpha, 20, 12, 6), 0, 0, W, H);
        }

        // 만점: 화려한 축하 화면
        void DrawPerfect(Graphics g, long now, bool blink)
        {
            Overlay(g, 200);
            Color[] cc = { C_GOLD, C_RED, Color.FromArgb(76, 201, 240), Color.FromArgb(181, 232, 83), Color.FromArgb(255, 143, 171), Color.White };
            foreach (float[] f in confetti)
            {
                f[1] += f[2];
                if (f[1] > H) { f[1] = -4; f[0] = (float)rnd.NextDouble() * W; }
                Rect(g, cc[(int)f[3]], (int)f[0], (int)f[1], 2, 2);
            }
            // 반짝이는 별
            int[] sx = { 12, 138, 24, 128, 8, 142, 40, 110 };
            int[] sy = { 54, 60, 150, 140, 100, 104, 190, 186 };
            for (int i = 0; i < sx.Length; i++)
            {
                if (((now / 160) + i) % 3 == 0) continue;
                Rect(g, C_GOLD, sx[i] - 2, sy[i], 5, 1);
                Rect(g, C_GOLD, sx[i], sy[i] - 2, 1, 5);
            }
            // 무지개색 물결 글자
            string msg = "CONGRATULATIONS!";
            float adv = TextWidth(g, "M", fMid);
            float x0 = 75 - adv * msg.Length / 2f;
            for (int i = 0; i < msg.Length; i++)
            {
                int dy = (int)Math.Round(Math.Sin(now / 140.0 + i * 0.6) * 3);
                Say(g, msg[i].ToString(), fMid, Hue(i * 24 + now / 4), x0 + i * adv, 56 + dy, false);
            }
            Say(g, "PERFECT " + Total() + "/" + Total(), fSmall, Color.White, 75, 80, true);
            DrawSprite(g, CROWN, 68, 95, 3, true);
            DrawSprite(g, MOLE, 57, 106, 3, true);
            Say(g, "made by", fSmall, Color.White, 75, 142, true);
            Say(g, "SJAHN", fBig, blink ? C_GOLD : Color.White, 75, 155, true);
            if (now - stateAt > 800 && blink) Say(g, "CLICK TO RETRY", fSmall, Color.White, 75, 192, true);
        }

        // 만점이 아닐 때: 일반 엔딩
        void DrawEnding(Graphics g, long now, bool blink)
        {
            Overlay(g, 170);
            Say(g, "GAME OVER", fBig, Color.White, 75, 54, true);
            Say(g, Hits() + " / " + Total(), fMid, C_GOLD, 75, 78, true);
            for (int i = 0; i < 3; i++)
            {
                Say(g, STAGE_NAME[i], fSmall, Color.White, 38, 100 + i * 13, false);
                string sc = stageHits[i] + "/" + STAGE_COUNT[i];
                Say(g, sc, fSmall, stageHits[i] == STAGE_COUNT[i] ? C_GOLD : Color.White, 112 - TextWidth(g, sc, fSmall), 100 + i * 13, false);
            }
            Say(g, (newBest ? "NEW BEST " : "BEST ") + Best, fSmall, newBest ? C_GOLD : Color.White, 75, 144, true);
            DrawSprite(g, MOLE_HIT, 57, 158, 3, true);
            if (now - stateAt > 800 && blink) Say(g, "CLICK TO RETRY", fSmall, Color.White, 75, 192, true);
        }
    }

    // ================= 캘린더: 데이터 =================

    public class CalEvent
    {
        public string Id;
        public DateTime Date;          // 날짜만
        public bool HasTime;
        public int Minutes;            // 하루 중 시각 (분)
        public int Color;              // 라벨 색 번호
        public List<int> Alarms = new List<int>();   // 몇 분 전
        public List<int> Fired = new List<int>();    // 이미 울린 알림
        public string Title = "";
        public string Body = "";

        // 알림 기준 시각 (종일 일정은 오전 9시)
        public DateTime Base()
        {
            return Date.AddMinutes(HasTime ? Minutes : 9 * 60);
        }

        public string TimeText()
        {
            if (!HasTime) return "종일";
            return Date.AddMinutes(Minutes).ToString("tt h:mm");
        }

        public override string ToString() { return Title; }
    }

    public class CalData
    {
        public static readonly Color[] LABEL = {
            Color.FromArgb(0, 103, 192), Color.FromArgb(0, 137, 123), Color.FromArgb(56, 142, 60), Color.FromArgb(217, 115, 13),
            Color.FromArgb(224, 62, 62), Color.FromArgb(194, 24, 91), Color.FromArgb(105, 64, 165), Color.FromArgb(110, 110, 110) };
        public static readonly string[] LABEL_NAME = { "파랑", "청록", "초록", "주황", "빨강", "분홍", "보라", "회색" };

        public List<CalEvent> Events = new List<CalEvent>();
        public Dictionary<DateTime, string> HolAdd = new Dictionary<DateTime, string>();
        public Dictionary<DateTime, bool> HolDel = new Dictionary<DateTime, bool>();
        Dictionary<int, Dictionary<DateTime, string>> holCache = new Dictionary<int, Dictionary<DateTime, string>>();
        string dir;

        public CalData(string dataDir) { dir = dataDir; }

        string EventFile() { return Path.Combine(dir, "_calendar.txt"); }
        string HolidayFile() { return Path.Combine(dir, "_holidays.txt"); }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "");
        }

        static string Unesc(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append("\r\n"); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string IntList(List<int> l)
        {
            StringBuilder sb = new StringBuilder();
            foreach (int v in l) { if (sb.Length > 0) sb.Append(','); sb.Append(v); }
            return sb.ToString();
        }

        static List<int> ParseInts(string s)
        {
            List<int> l = new List<int>();
            foreach (string p in s.Split(','))
            {
                int v;
                if (int.TryParse(p, out v) && l.IndexOf(v) < 0) l.Add(v);
            }
            return l;
        }

        public void Load()
        {
            Events.Clear();
            try
            {
                if (File.Exists(EventFile()))
                {
                    foreach (string line in File.ReadAllLines(EventFile(), Encoding.UTF8))
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 8) continue;
                        CalEvent ev = new CalEvent();
                        ev.Id = f[0];
                        DateTime d;
                        if (!DateTime.TryParseExact(f[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                        ev.Date = d;
                        if (f[2].Length == 5)
                        {
                            int h, m;
                            if (int.TryParse(f[2].Substring(0, 2), out h) && int.TryParse(f[2].Substring(3, 2), out m))
                            {
                                ev.HasTime = true;
                                ev.Minutes = h * 60 + m;
                            }
                        }
                        int col;
                        int.TryParse(f[3], out col);
                        ev.Color = Math.Max(0, Math.Min(LABEL.Length - 1, col));
                        ev.Alarms = ParseInts(f[4]);
                        ev.Fired = ParseInts(f[5]);
                        ev.Title = Unesc(f[6]);
                        ev.Body = Unesc(f[7]);
                        Events.Add(ev);
                    }
                }
            }
            catch { }
            LoadHolidays();
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            foreach (CalEvent ev in Events)
            {
                sb.Append(ev.Id).Append('\t');
                sb.Append(ev.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\t');
                if (ev.HasTime) sb.Append((ev.Minutes / 60).ToString("00")).Append(':').Append((ev.Minutes % 60).ToString("00"));
                sb.Append('\t').Append(ev.Color).Append('\t');
                sb.Append(IntList(ev.Alarms)).Append('\t').Append(IntList(ev.Fired)).Append('\t');
                sb.Append(Esc(ev.Title)).Append('\t').Append(Esc(ev.Body)).Append("\r\n");
            }
            WriteSafe(EventFile(), sb.ToString());
        }

        static void WriteSafe(string path, string text)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
        }

        public List<CalEvent> EventsOn(DateTime day)
        {
            List<CalEvent> l = new List<CalEvent>();
            foreach (CalEvent ev in Events) if (ev.Date == day.Date) l.Add(ev);
            l.Sort(CompareEvents);
            return l;
        }

        public static int CompareEvents(CalEvent a, CalEvent b)
        {
            int c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            int ta = a.HasTime ? a.Minutes : -1;
            int tb = b.HasTime ? b.Minutes : -1;
            if (ta != tb) return ta.CompareTo(tb);
            return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
        }

        public List<CalEvent> Search(string q)
        {
            List<CalEvent> l = new List<CalEvent>();
            foreach (CalEvent ev in Events)
            {
                if (ev.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    ev.Body.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) l.Add(ev);
            }
            l.Sort(CompareEvents);
            return l;
        }

        // ---------- 공휴일 (직접 추가 / 삭제) ----------

        void LoadHolidays()
        {
            HolAdd.Clear();
            HolDel.Clear();
            holCache.Clear();
            try
            {
                if (!File.Exists(HolidayFile())) return;
                foreach (string line in File.ReadAllLines(HolidayFile(), Encoding.UTF8))
                {
                    if (line.Length < 11) continue;
                    DateTime d;
                    if (!DateTime.TryParseExact(line.Substring(1, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                    if (line[0] == '+')
                    {
                        string name = line.Length > 12 ? line.Substring(12) : "공휴일";
                        HolAdd[d] = name;
                    }
                    else if (line[0] == '-') HolDel[d] = true;
                }
            }
            catch { }
        }

        public void SaveHolidays()
        {
            holCache.Clear();
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<DateTime, bool> kv in HolDel)
                sb.Append('-').Append(kv.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\r\n");
            foreach (KeyValuePair<DateTime, string> kv in HolAdd)
                sb.Append('+').Append(kv.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\t').Append(kv.Value.Replace("\t", " ")).Append("\r\n");
            WriteSafe(HolidayFile(), sb.ToString());
        }

        // 그 해의 공휴일 (기본 + 직접 추가 - 삭제)
        public Dictionary<DateTime, string> Holidays(int year)
        {
            Dictionary<DateTime, string> r;
            if (holCache.TryGetValue(year, out r)) return r;
            r = new Dictionary<DateTime, string>();
            foreach (KeyValuePair<DateTime, string> kv in KHoliday.Builtin(year))
            {
                if (HolDel.ContainsKey(kv.Key)) continue;
                string old;
                if (r.TryGetValue(kv.Key, out old)) r[kv.Key] = old + ", " + kv.Value;
                else r[kv.Key] = kv.Value;
            }
            foreach (KeyValuePair<DateTime, string> kv in HolAdd)
            {
                if (kv.Key.Year != year) continue;
                string old;
                if (r.TryGetValue(kv.Key, out old)) r[kv.Key] = old + ", " + kv.Value;
                else r[kv.Key] = kv.Value;
            }
            holCache[year] = r;
            return r;
        }

        public string HolidayName(DateTime d)
        {
            string n;
            if (Holidays(d.Year).TryGetValue(d.Date, out n)) return n;
            return null;
        }
    }

    // ================= 한국 공휴일 (기본값) =================
    // 음력 날짜는 윈도우 .NET 의 한국 음력 달력으로 계산 (2050년까지)
    // 대체공휴일은 현재 규정대로 계산. 임시공휴일은 직접 추가.

    public static class KHoliday
    {
        static KoreanLunisolarCalendar kcal = new KoreanLunisolarCalendar();
        static Dictionary<int, List<KeyValuePair<DateTime, string>>> cache = new Dictionary<int, List<KeyValuePair<DateTime, string>>>();

        static bool Lunar(int y, int m, int d, out DateTime r)
        {
            r = DateTime.MinValue;
            try
            {
                int leap = kcal.GetLeapMonth(y);
                int mm = (leap > 0 && m >= leap) ? m + 1 : m;
                r = kcal.ToDateTime(y, mm, d, 0, 0, 0, 0);
                return true;
            }
            catch { return false; }
        }

        static void Add(List<KeyValuePair<DateTime, string>> l, DateTime d, string name)
        {
            l.Add(new KeyValuePair<DateTime, string>(d.Date, name));
        }

        static int CountOn(List<KeyValuePair<DateTime, string>> l, DateTime d)
        {
            int n = 0;
            foreach (KeyValuePair<DateTime, string> kv in l) if (kv.Key == d.Date) n++;
            return n;
        }

        static bool Weekend(DateTime d)
        {
            return d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
        }

        // from 다음 날부터 주말·공휴일이 아닌 첫 날을 대체공휴일로
        static void AddSub(List<KeyValuePair<DateTime, string>> l, DateTime from, string name)
        {
            DateTime d = from.Date.AddDays(1);
            while (Weekend(d) || CountOn(l, d) > 0) d = d.AddDays(1);
            Add(l, d, name + " 대체공휴일");
        }

        // 설날·추석 연휴: 일요일이나 다른 공휴일과 겹친 날 수만큼
        static void GroupSub(List<KeyValuePair<DateTime, string>> l, DateTime start, string name)
        {
            int over = 0;
            for (int i = 0; i < 3; i++)
            {
                DateTime d = start.AddDays(i);
                if (d.DayOfWeek == DayOfWeek.Sunday || CountOn(l, d) > 1) over++;
            }
            for (int k = 0; k < over; k++) AddSub(l, start.AddDays(2), name);
        }

        static int CompareKv(KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b)
        {
            return a.Key.CompareTo(b.Key);
        }

        public static List<KeyValuePair<DateTime, string>> Builtin(int y)
        {
            List<KeyValuePair<DateTime, string>> l;
            if (cache.TryGetValue(y, out l)) return l;
            l = new List<KeyValuePair<DateTime, string>>();
            if (y < 1950 || y > 9998) { cache[y] = l; return l; }

            DateTime seol, buddha, chuseok;
            bool hasSeol = Lunar(y, 1, 1, out seol);
            bool hasBuddha = Lunar(y, 4, 8, out buddha);
            bool hasChu = Lunar(y, 8, 15, out chuseok);

            Add(l, new DateTime(y, 1, 1), "신정");
            if (hasSeol) { Add(l, seol.AddDays(-1), "설날 연휴"); Add(l, seol, "설날"); Add(l, seol.AddDays(1), "설날 연휴"); }
            Add(l, new DateTime(y, 3, 1), "삼일절");
            Add(l, new DateTime(y, 5, 5), "어린이날");
            if (hasBuddha) Add(l, buddha, "부처님오신날");
            Add(l, new DateTime(y, 6, 6), "현충일");
            Add(l, new DateTime(y, 8, 15), "광복절");
            if (hasChu) { Add(l, chuseok.AddDays(-1), "추석 연휴"); Add(l, chuseok, "추석"); Add(l, chuseok.AddDays(1), "추석 연휴"); }
            Add(l, new DateTime(y, 10, 3), "개천절");
            Add(l, new DateTime(y, 10, 9), "한글날");
            Add(l, new DateTime(y, 12, 25), "성탄절");

            // 이미 정해진 선거일
            if (y == 2026) Add(l, new DateTime(2026, 6, 3), "전국동시지방선거");
            if (y == 2028) Add(l, new DateTime(2028, 4, 12), "국회의원선거");

            // 대체공휴일
            if (y >= 2014)
            {
                if (hasSeol) GroupSub(l, seol.AddDays(-1), "설날");
                if (hasChu) GroupSub(l, chuseok.AddDays(-1), "추석");
                DateTime kid = new DateTime(y, 5, 5);
                if (Weekend(kid) || CountOn(l, kid) > 1) AddSub(l, kid, "어린이날");
            }
            if (y >= 2021)
            {
                DateTime[] nat = { new DateTime(y, 3, 1), new DateTime(y, 8, 15), new DateTime(y, 10, 3), new DateTime(y, 10, 9) };
                string[] natName = { "삼일절", "광복절", "개천절", "한글날" };
                for (int i = 0; i < nat.Length; i++)
                    if (Weekend(nat[i])) AddSub(l, nat[i], natName[i]);
            }
            if (y >= 2023)
            {
                if (hasBuddha && Weekend(buddha)) AddSub(l, buddha, "부처님오신날");
                DateTime xmas = new DateTime(y, 12, 25);
                if (Weekend(xmas)) AddSub(l, xmas, "성탄절");
            }

            // 다른 해로 넘어간 날 (예: 1월 설 연휴 전날이 전년도 12월) 은 빼기
            for (int i = l.Count - 1; i >= 0; i--) if (l[i].Key.Year != y) l.RemoveAt(i);
            l.Sort(CompareKv);
            cache[y] = l;
            return l;
        }
    }

    // ================= 캘린더 창 =================

    public class MonthGrid : Panel
    {
        public MonthGrid()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    public class CalendarForm : Form
    {
        CalData cal;
        float S;
        int year, month;
        DateTime selected;
        bool searching = false;

        MonthGrid grid;
        Label monthLabel, dayHeader;
        TextBox searchBox;
        ListBox list;
        Button prevBtn, nextBtn, todayBtn, addBtn, holBtn, editBtn, delBtn, newBtn;
        Panel top, right;
        FlowLayoutPanel rightBtns;
        Font fDay, fSmall, fChip, fHead, fBold;
        Dictionary<DateTime, List<CalEvent>> monthEvents = new Dictionary<DateTime, List<CalEvent>>();

        Color cBack, cText, cMuted, cLine, cToday, cOther, cAccent;
        static readonly Color C_SUN = Color.FromArgb(224, 62, 62);
        static readonly Color C_SAT_LIGHT = Color.FromArgb(11, 107, 203);
        static readonly Color C_SAT_DARK = Color.FromArgb(100, 165, 245);
        Color C_SAT { get { return Skin.Dark ? C_SAT_DARK : C_SAT_LIGHT; } }

        public event EventHandler Changed;

        int Px(int v) { return (int)(v * S); }

        public CalendarForm(CalData data, float scale)
        {
            cal = data;
            S = scale;
            Text = "AHNNOTE 캘린더";
            Font = new Font("Malgun Gothic", 9f);
            ClientSize = new Size(Px(940), Px(620));
            MinimumSize = new Size(Px(640), Px(440));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            try { Icon = Icon.FromHandle(AppIcon.DrawA(32).GetHicon()); } catch { }

            fDay = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
            fSmall = new Font("Malgun Gothic", 8f);
            fChip = new Font("Malgun Gothic", 8f);
            fHead = new Font("Malgun Gothic", 14f, FontStyle.Bold);
            fBold = new Font("Malgun Gothic", 9f, FontStyle.Bold);

            // 위쪽 줄
            top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = Px(48);
            top.Padding = new Padding(Px(10), Px(8), Px(10), Px(6));
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            prevBtn = TopButton("◀", Px(34));
            monthLabel = new Label();
            monthLabel.AutoSize = false;
            monthLabel.Size = new Size(Px(150), Px(32));
            monthLabel.TextAlign = ContentAlignment.MiddleCenter;
            monthLabel.Font = fHead;
            nextBtn = TopButton("▶", Px(34));
            todayBtn = TopButton("오늘", Px(56));
            addBtn = TopButton("+ 일정", Px(70));
            holBtn = TopButton("공휴일 관리", Px(96));
            Label sl = new Label();
            sl.Text = "검색";
            sl.AutoSize = false;
            sl.Size = new Size(Px(40), Px(32));
            sl.TextAlign = ContentAlignment.MiddleRight;
            searchBox = new TextBox();
            searchBox.Width = Px(170);
            searchBox.Margin = new Padding(Px(4), Px(5), 0, 0);
            flow.Controls.Add(prevBtn);
            flow.Controls.Add(monthLabel);
            flow.Controls.Add(nextBtn);
            flow.Controls.Add(todayBtn);
            flow.Controls.Add(addBtn);
            flow.Controls.Add(holBtn);
            flow.Controls.Add(sl);
            flow.Controls.Add(searchBox);
            top.Controls.Add(flow);

            prevBtn.Click += new EventHandler(Prev_Click);
            nextBtn.Click += new EventHandler(Next_Click);
            todayBtn.Click += new EventHandler(Today_Click);
            addBtn.Click += new EventHandler(Add_Click);
            holBtn.Click += new EventHandler(Hol_Click);
            searchBox.TextChanged += new EventHandler(Search_Changed);

            // 오른쪽: 선택한 날 일정 / 검색 결과
            right = new Panel();
            right.Dock = DockStyle.Right;
            right.Width = Px(290);
            right.Padding = new Padding(Px(8), 0, Px(10), Px(10));
            dayHeader = new Label();
            dayHeader.Dock = DockStyle.Top;
            dayHeader.Height = Px(46);
            dayHeader.TextAlign = ContentAlignment.MiddleLeft;
            dayHeader.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
            list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Px(48);
            list.IntegralHeight = false;
            list.DrawItem += new DrawItemEventHandler(List_DrawItem);
            list.DoubleClick += new EventHandler(Edit_Click);
            list.SelectedIndexChanged += new EventHandler(List_Selected);
            list.KeyDown += new KeyEventHandler(List_KeyDown);
            rightBtns = new FlowLayoutPanel();
            rightBtns.Dock = DockStyle.Bottom;
            rightBtns.Height = Px(40);
            rightBtns.Padding = new Padding(0, Px(6), 0, 0);
            newBtn = TopButton("추가", Px(70));
            editBtn = TopButton("수정", Px(70));
            delBtn = TopButton("삭제", Px(70));
            rightBtns.Controls.Add(newBtn);
            rightBtns.Controls.Add(editBtn);
            rightBtns.Controls.Add(delBtn);
            newBtn.Click += new EventHandler(Add_Click);
            editBtn.Click += new EventHandler(Edit_Click);
            delBtn.Click += new EventHandler(Del_Click);
            right.Controls.Add(list);
            right.Controls.Add(dayHeader);
            right.Controls.Add(rightBtns);

            // 달력
            grid = new MonthGrid();
            grid.Dock = DockStyle.Fill;
            grid.Paint += new PaintEventHandler(Grid_Paint);
            grid.MouseDown += new MouseEventHandler(Grid_MouseDown);
            grid.MouseDoubleClick += new MouseEventHandler(Grid_DoubleClick);

            Controls.Add(grid);
            Controls.Add(right);
            Controls.Add(top);

            DateTime today = DateTime.Today;
            year = today.Year;
            month = today.Month;
            selected = today;
            ApplyColors();
            Reload();
        }

        Button TopButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(width, Px(30));
            b.Margin = new Padding(Px(2), Px(1), Px(2), 0);
            return b;
        }

        public void ApplyColors()
        {
            bool dark = Skin.Dark;
            cBack = dark ? Color.FromArgb(32, 32, 32) : Color.White;
            cText = dark ? Color.FromArgb(232, 232, 232) : Color.FromArgb(32, 32, 32);
            cMuted = dark ? Color.FromArgb(120, 124, 130) : Color.FromArgb(170, 174, 180);
            cLine = dark ? Color.FromArgb(55, 55, 58) : Color.FromArgb(222, 226, 232);
            cToday = dark ? Color.FromArgb(30, 50, 75) : Color.FromArgb(232, 242, 252);
            cOther = dark ? Color.FromArgb(27, 27, 29) : Color.FromArgb(248, 249, 251);
            cAccent = dark ? Color.FromArgb(96, 180, 255) : Color.FromArgb(0, 103, 192);
            Skin.Apply(this);
            BackColor = cBack;
            top.BackColor = dark ? Color.FromArgb(43, 43, 45) : Color.White;
            right.BackColor = cBack;
            list.BackColor = cBack;
            list.ForeColor = cText;
            dayHeader.ForeColor = cText;
            monthLabel.ForeColor = cText;
            grid.BackColor = cBack;
            Skin.ScrollBars(list, dark);
            grid.Invalidate();
            list.Invalidate();
        }

        // 데이터가 바뀌었을 때
        public void Reload()
        {
            monthEvents.Clear();
            foreach (CalEvent ev in cal.Events)
            {
                List<CalEvent> l;
                if (!monthEvents.TryGetValue(ev.Date, out l)) { l = new List<CalEvent>(); monthEvents[ev.Date] = l; }
                l.Add(ev);
            }
            foreach (List<CalEvent> l in monthEvents.Values) l.Sort(CalData.CompareEvents);
            monthLabel.Text = year + "년 " + month + "월";
            FillList();
            grid.Invalidate();
        }

        public void GoTo(DateTime d)
        {
            year = d.Year;
            month = d.Month;
            selected = d.Date;
            Reload();
        }

        void FillList()
        {
            CalEvent keep = list.SelectedItem as CalEvent;
            list.BeginUpdate();
            list.Items.Clear();
            List<CalEvent> items;
            if (searching)
            {
                items = cal.Search(searchBox.Text.Trim());
                dayHeader.Text = "검색 결과 " + items.Count + "개";
            }
            else
            {
                items = cal.EventsOn(selected);
                string h = cal.HolidayName(selected);
                dayHeader.Text = selected.ToString("M월 d일 dddd") + (h != null ? "   " + h : "");
            }
            foreach (CalEvent ev in items) list.Items.Add(ev);
            if (keep != null && list.Items.Contains(keep)) list.SelectedItem = keep;
            list.EndUpdate();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool has = list.SelectedItem != null;
            editBtn.Enabled = has;
            delBtn.Enabled = has;
        }

        // ---------- 달력 그리기 ----------

        DateTime GridStart()
        {
            DateTime first = new DateTime(year, month, 1);
            return first.AddDays(-(int)first.DayOfWeek);
        }

        Rectangle CellRect(int i, int headH, float cw, float ch)
        {
            int col = i % 7, row = i / 7;
            int x = (int)(col * cw), y = headH + (int)(row * ch);
            return new Rectangle(x, y, (int)((col + 1) * cw) - x, headH + (int)((row + 1) * ch) - y);
        }

        void Grid_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = grid.ClientSize.Width - 1, h = grid.ClientSize.Height - 1;
            int headH = Px(28);
            float cw = w / 7f, ch = (h - headH) / 6f;
            string[] wd = { "일", "월", "화", "수", "목", "금", "토" };
            using (Pen line = new Pen(cLine))
            {
                for (int c = 0; c < 7; c++)
                {
                    Rectangle r = new Rectangle((int)(c * cw), 0, (int)cw, headH);
                    Color col = c == 0 ? C_SUN : c == 6 ? C_SAT : cMuted;
                    TextRenderer.DrawText(g, wd[c], fSmall, r, col, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                DateTime start = GridStart();
                DateTime today = DateTime.Today;
                for (int i = 0; i < 42; i++)
                {
                    DateTime d = start.AddDays(i);
                    Rectangle r = CellRect(i, headH, cw, ch);
                    bool other = d.Month != month;
                    Color back = other ? cOther : cBack;
                    if (d == today) back = cToday;
                    using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, r);
                    g.DrawRectangle(line, r);
                    if (d == selected)
                    {
                        using (Pen p = new Pen(cAccent, Math.Max(2, Px(2))))
                            g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                    }

                    string hol = cal.HolidayName(d);
                    Color dc = cText;
                    if (d.DayOfWeek == DayOfWeek.Sunday || hol != null) dc = C_SUN;
                    else if (d.DayOfWeek == DayOfWeek.Saturday) dc = C_SAT;
                    if (other) dc = Color.FromArgb(110, dc);
                    Rectangle nr = new Rectangle(r.X + Px(6), r.Y + Px(3), Px(26), Px(18));
                    TextRenderer.DrawText(g, d.Day.ToString(), fDay, nr, dc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (hol != null)
                    {
                        Rectangle hr = new Rectangle(r.X + Px(30), r.Y + Px(4), r.Width - Px(34), Px(16));
                        TextRenderer.DrawText(g, hol, fSmall, hr, other ? Color.FromArgb(120, C_SUN) : C_SUN,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    }

                    // 일정 라벨
                    List<CalEvent> evs;
                    if (!monthEvents.TryGetValue(d, out evs)) continue;
                    int chipH = Px(17), gap = Px(2);
                    int y0 = r.Y + Px(23);
                    int fit = Math.Max(0, (r.Bottom - Px(2) - y0) / (chipH + gap));
                    int show = evs.Count <= fit ? evs.Count : Math.Max(0, fit - 1);
                    for (int k = 0; k < show; k++)
                    {
                        CalEvent ev = evs[k];
                        Rectangle cr = new Rectangle(r.X + Px(3), y0 + k * (chipH + gap), r.Width - Px(6), chipH);
                        Color lc = CalData.LABEL[ev.Color];
                        if (other) lc = Color.FromArgb(140, lc);
                        using (SolidBrush b = new SolidBrush(lc)) g.FillRectangle(b, cr);
                        string t = (ev.HasTime ? ev.Date.AddMinutes(ev.Minutes).ToString("H:mm") + " " : "") + ev.Title;
                        Rectangle tr = new Rectangle(cr.X + Px(3), cr.Y, cr.Width - Px(4), cr.Height);
                        TextRenderer.DrawText(g, t, fChip, tr, Color.White,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    }
                    if (show < evs.Count && fit > 0)
                    {
                        Rectangle mr = new Rectangle(r.X + Px(5), y0 + show * (chipH + gap), r.Width - Px(8), chipH);
                        TextRenderer.DrawText(g, "+" + (evs.Count - show) + "개 더", fSmall, mr, cMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                }
            }
        }

        DateTime? DateAt(Point p)
        {
            int w = grid.ClientSize.Width - 1, h = grid.ClientSize.Height - 1;
            int headH = Px(28);
            if (p.Y < headH) return null;
            float cw = w / 7f, ch = (h - headH) / 6f;
            int col = Math.Min(6, (int)(p.X / cw));
            int row = Math.Min(5, (int)((p.Y - headH) / ch));
            return GridStart().AddDays(row * 7 + col);
        }

        void Grid_MouseDown(object sender, MouseEventArgs e)
        {
            DateTime? d = DateAt(e.Location);
            if (!d.HasValue) return;
            if (searching) { searchBox.Text = ""; }
            selected = d.Value;
            if (selected.Month != month || selected.Year != year) { year = selected.Year; month = selected.Month; Reload(); }
            else { FillList(); grid.Invalidate(); }
        }

        void Grid_DoubleClick(object sender, MouseEventArgs e)
        {
            DateTime? d = DateAt(e.Location);
            if (!d.HasValue) return;
            selected = d.Value;
            EditEvent(null);
        }

        // ---------- 오른쪽 목록 ----------

        void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            CalEvent ev = (CalEvent)list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = sel ? (Skin.Dark ? Color.FromArgb(38, 60, 88) : Color.FromArgb(217, 233, 250)) : cBack;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            using (SolidBrush b = new SolidBrush(CalData.LABEL[ev.Color]))
                e.Graphics.FillRectangle(b, e.Bounds.X + Px(2), e.Bounds.Y + Px(7), Px(5), e.Bounds.Height - Px(14));
            int x = e.Bounds.X + Px(14);
            Rectangle r1 = new Rectangle(x, e.Bounds.Y + Px(5), e.Bounds.Right - x - Px(4), Px(20));
            Rectangle r2 = new Rectangle(x, e.Bounds.Y + Px(25), e.Bounds.Right - x - Px(4), Px(18));
            TextRenderer.DrawText(e.Graphics, ev.Title, fBold, r1, cText,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            string sub = (searching ? ev.Date.ToString("yyyy-MM-dd (ddd)") + "  " : "") + ev.TimeText();
            if (ev.Alarms.Count > 0) sub += "   알림 " + ev.Alarms.Count + "개";
            if (ev.Body.Length > 0 && !searching) sub += "   " + ev.Body.Replace("\r\n", " ");
            TextRenderer.DrawText(e.Graphics, sub, fSmall, r2, cMuted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        void List_Selected(object sender, EventArgs e)
        {
            UpdateButtons();
            CalEvent ev = list.SelectedItem as CalEvent;
            if (searching && ev != null && (ev.Date.Year != year || ev.Date.Month != month || ev.Date != selected))
            {
                year = ev.Date.Year;
                month = ev.Date.Month;
                selected = ev.Date;
                monthLabel.Text = year + "년 " + month + "월";
                grid.Invalidate();
            }
        }

        void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { Del_Click(sender, e); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Edit_Click(sender, e); e.Handled = true; }
        }

        // ---------- 버튼 ----------

        void Prev_Click(object sender, EventArgs e) { MoveMonth(-1); }
        void Next_Click(object sender, EventArgs e) { MoveMonth(1); }
        void Today_Click(object sender, EventArgs e) { searchBox.Text = ""; GoTo(DateTime.Today); }

        void MoveMonth(int delta)
        {
            DateTime d = new DateTime(year, month, 1).AddMonths(delta);
            year = d.Year;
            month = d.Month;
            int day = Math.Min(selected.Day, DateTime.DaysInMonth(year, month));
            selected = new DateTime(year, month, day);
            Reload();
        }

        void Search_Changed(object sender, EventArgs e)
        {
            searching = searchBox.Text.Trim().Length > 0;
            FillList();
        }

        void Add_Click(object sender, EventArgs e) { EditEvent(null); }

        void Edit_Click(object sender, EventArgs e)
        {
            CalEvent ev = list.SelectedItem as CalEvent;
            if (ev != null) EditEvent(ev);
        }

        void Del_Click(object sender, EventArgs e)
        {
            CalEvent ev = list.SelectedItem as CalEvent;
            if (ev == null) return;
            if (MessageBox.Show(this, "'" + ev.Title + "' 일정을 삭제할까요?", "AHNNOTE",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            cal.Events.Remove(ev);
            cal.Save();
            Reload();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        void EditEvent(CalEvent ev)
        {
            using (EventForm f = new EventForm(ev, ev == null ? selected : ev.Date, S))
            {
                f.TopMost = TopMost;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (ev == null)
                {
                    ev = f.Result;
                    ev.Id = DateTime.Now.Ticks.ToString();
                    cal.Events.Add(ev);
                }
                else f.CopyTo(ev);
                // 이미 지난 알림은 울리지 않음
                ev.Fired.Clear();
                DateTime b = ev.Base();
                foreach (int m in ev.Alarms) if (b.AddMinutes(-m) <= DateTime.Now) ev.Fired.Add(m);
                cal.Save();
                if (!searching) { selected = ev.Date; year = ev.Date.Year; month = ev.Date.Month; }
                Reload();
                list.SelectedItem = ev;
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }

        void Hol_Click(object sender, EventArgs e)
        {
            using (HolidayForm f = new HolidayForm(cal, year, S))
            {
                f.TopMost = TopMost;
                f.ShowDialog(this);
            }
            Reload();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { searchBox.Focus(); searchBox.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { EditEvent(null); return true; }
            if (keyData == Keys.PageUp) { MoveMonth(-1); return true; }
            if (keyData == Keys.PageDown) { MoveMonth(1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // ================= 일정 입력 창 =================

    public class EventForm : Form
    {
        public CalEvent Result;
        TextBox titleBox, bodyBox;
        DateTimePicker datePick, timePick;
        CheckBox timeChk;
        Button[] colorBtns;
        int color = 0;
        ComboBox presetBox, unitBox;
        NumericUpDown numBox;
        ListBox alarmList;
        Button alarmAdd, alarmDel;
        List<int> alarms = new List<int>();
        float S;

        static readonly int[] PRESET = { 30, 60, 1440, 2880, 10080, -1 };
        static readonly string[] PRESET_NAME = { "30분 전", "1시간 전", "1일 전", "2일 전", "7일 전", "직접 설정" };

        int Px(int v) { return (int)(v * S); }

        public static string AlarmText(int m)
        {
            if (m == 0) return "정시";
            if (m % 1440 == 0) return (m / 1440) + "일 전";
            if (m % 60 == 0) return (m / 60) + "시간 전";
            return m + "분 전";
        }

        Label AddLabel(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(Px(x), Px(y), Px(w), Px(22));
            l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l);
            return l;
        }

        public EventForm(CalEvent ev, DateTime date, float scale)
        {
            S = scale;
            Text = ev == null ? "새 일정" : "일정 수정";
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Px(420), Px(500));

            AddLabel("제목", 14, 14, 60);
            titleBox = new TextBox();
            titleBox.SetBounds(Px(80), Px(14), Px(326), Px(24));
            Controls.Add(titleBox);

            AddLabel("라벨 색", 14, 48, 60);
            colorBtns = new Button[CalData.LABEL.Length];
            for (int i = 0; i < colorBtns.Length; i++)
            {
                Button b = new Button();
                b.SetBounds(Px(80 + i * 30), Px(48), Px(24), Px(22));
                b.FlatStyle = FlatStyle.Flat;
                b.Font = new Font("Segoe UI Symbol", 9f, FontStyle.Bold);
                b.Padding = new Padding(0);
                b.BackColor = CalData.LABEL[i];
                b.Tag = i;
                b.TabStop = false;
                b.Click += new EventHandler(Color_Click);
                colorBtns[i] = b;
                Controls.Add(b);
            }

            AddLabel("날짜", 14, 82, 60);
            datePick = new DateTimePicker();
            datePick.Format = DateTimePickerFormat.Long;
            datePick.SetBounds(Px(80), Px(82), Px(220), Px(24));
            Controls.Add(datePick);

            timeChk = new CheckBox();
            timeChk.Text = "시간";
            timeChk.SetBounds(Px(14), Px(114), Px(62), Px(24));
            timeChk.CheckedChanged += new EventHandler(TimeChk_Changed);
            Controls.Add(timeChk);
            timePick = new DateTimePicker();
            timePick.Format = DateTimePickerFormat.Custom;
            timePick.CustomFormat = "tt h:mm";
            timePick.ShowUpDown = true;
            timePick.SetBounds(Px(80), Px(114), Px(120), Px(24));
            Controls.Add(timePick);
            Label allDay = AddLabel("체크하지 않으면 종일 일정", 208, 114, 200);
            allDay.ForeColor = Color.Gray;

            AddLabel("내용", 14, 148, 60);
            bodyBox = new TextBox();
            bodyBox.Multiline = true;
            bodyBox.ScrollBars = ScrollBars.Vertical;
            bodyBox.AcceptsReturn = true;
            bodyBox.SetBounds(Px(80), Px(148), Px(326), Px(110));
            Controls.Add(bodyBox);

            AddLabel("알림", 14, 272, 60);
            presetBox = new ComboBox();
            presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetBox.Items.AddRange(PRESET_NAME);
            presetBox.SelectedIndex = 0;
            presetBox.SetBounds(Px(80), Px(272), Px(100), Px(24));
            presetBox.SelectedIndexChanged += new EventHandler(Preset_Changed);
            Controls.Add(presetBox);
            numBox = new NumericUpDown();
            numBox.Minimum = 1;
            numBox.Maximum = 999;
            numBox.Value = 10;
            numBox.SetBounds(Px(186), Px(272), Px(56), Px(24));
            Controls.Add(numBox);
            unitBox = new ComboBox();
            unitBox.DropDownStyle = ComboBoxStyle.DropDownList;
            unitBox.Items.AddRange(new string[] { "분 전", "시간 전", "일 전" });
            unitBox.SelectedIndex = 0;
            unitBox.SetBounds(Px(246), Px(272), Px(76), Px(24));
            Controls.Add(unitBox);
            alarmAdd = new Button();
            alarmAdd.Text = "추가";
            alarmAdd.SetBounds(Px(328), Px(271), Px(78), Px(26));
            alarmAdd.Click += new EventHandler(AlarmAdd_Click);
            Controls.Add(alarmAdd);

            alarmList = new ListBox();
            alarmList.SetBounds(Px(80), Px(304), Px(242), Px(96));
            alarmList.IntegralHeight = false;
            Controls.Add(alarmList);
            alarmDel = new Button();
            alarmDel.Text = "알림 삭제";
            alarmDel.SetBounds(Px(328), Px(304), Px(78), Px(26));
            alarmDel.Click += new EventHandler(AlarmDel_Click);
            Controls.Add(alarmDel);
            Label note = AddLabel("최대 5개. 종일 일정은 그날 오전 9시 기준", 80, 404, 326);
            note.ForeColor = Color.Gray;

            Button ok = new Button();
            ok.Text = "저장";
            ok.SetBounds(Px(240), Px(456), Px(80), Px(30));
            ok.Click += new EventHandler(Ok_Click);
            Controls.Add(ok);
            Button cancel = new Button();
            cancel.Text = "취소";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(Px(326), Px(456), Px(80), Px(30));
            Controls.Add(cancel);
            AcceptButton = null;
            CancelButton = cancel;

            // 값 채우기
            datePick.Value = date.Date;
            timePick.Value = DateTime.Today.AddHours(9);
            if (ev != null)
            {
                titleBox.Text = ev.Title;
                bodyBox.Text = ev.Body;
                color = ev.Color;
                timeChk.Checked = ev.HasTime;
                if (ev.HasTime) timePick.Value = DateTime.Today.AddMinutes(ev.Minutes);
                alarms.AddRange(ev.Alarms);
            }
            TimeChk_Changed(null, EventArgs.Empty);
            Preset_Changed(null, EventArgs.Empty);
            ShowColors();
            ShowAlarms();
            Skin.Apply(this);
            ShowColors();
            ActiveControl = titleBox;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            titleBox.Focus();
            titleBox.SelectAll();
        }

        void ShowColors()
        {
            for (int i = 0; i < colorBtns.Length; i++)
            {
                colorBtns[i].BackColor = CalData.LABEL[i];
                colorBtns[i].ForeColor = Color.White;
                colorBtns[i].Text = i == color ? "✓" : "";
                colorBtns[i].FlatAppearance.BorderSize = i == color ? Math.Max(2, Px(3)) : 0;
                colorBtns[i].FlatAppearance.BorderColor = Skin.Dark ? Color.White : Color.Black;
            }
        }

        void ShowAlarms()
        {
            alarms.Sort();
            alarmList.Items.Clear();
            foreach (int m in alarms) alarmList.Items.Add(AlarmText(m));
            alarmAdd.Enabled = alarms.Count < 5;
        }

        void Color_Click(object sender, EventArgs e)
        {
            color = (int)((Button)sender).Tag;
            ShowColors();
        }

        void TimeChk_Changed(object sender, EventArgs e) { timePick.Enabled = timeChk.Checked; }

        void Preset_Changed(object sender, EventArgs e)
        {
            bool custom = PRESET[presetBox.SelectedIndex] < 0;
            numBox.Enabled = custom;
            unitBox.Enabled = custom;
        }

        void AlarmAdd_Click(object sender, EventArgs e)
        {
            if (alarms.Count >= 5) return;
            int m = PRESET[presetBox.SelectedIndex];
            if (m < 0)
            {
                int n = (int)numBox.Value;
                m = unitBox.SelectedIndex == 2 ? n * 1440 : unitBox.SelectedIndex == 1 ? n * 60 : n;
            }
            if (alarms.IndexOf(m) < 0) alarms.Add(m);
            ShowAlarms();
        }

        void AlarmDel_Click(object sender, EventArgs e)
        {
            int i = alarmList.SelectedIndex;
            if (i < 0) return;
            alarms.RemoveAt(i);
            ShowAlarms();
        }

        void Ok_Click(object sender, EventArgs e)
        {
            if (titleBox.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "제목을 입력하세요.", "AHNNOTE");
                titleBox.Focus();
                return;
            }
            Result = new CalEvent();
            CopyTo(Result);
            DialogResult = DialogResult.OK;
            Close();
        }

        public void CopyTo(CalEvent ev)
        {
            ev.Title = titleBox.Text.Trim();
            ev.Body = bodyBox.Text;
            ev.Date = datePick.Value.Date;
            ev.HasTime = timeChk.Checked;
            ev.Minutes = timePick.Value.Hour * 60 + timePick.Value.Minute;
            ev.Color = color;
            ev.Alarms = new List<int>(alarms);
        }
    }

    // ================= 공휴일 관리 창 =================

    public class HolidayForm : Form
    {
        CalData cal;
        NumericUpDown yearBox;
        ListBox list;
        DateTimePicker addDate;
        TextBox addName;
        List<DateTime> keys = new List<DateTime>();
        float S;

        int Px(int v) { return (int)(v * S); }

        public HolidayForm(CalData data, int year, float scale)
        {
            cal = data;
            S = scale;
            Text = "공휴일 관리";
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Px(440), Px(470));

            Label yl = new Label();
            yl.Text = "연도";
            yl.SetBounds(Px(14), Px(16), Px(40), Px(22));
            Controls.Add(yl);
            yearBox = new NumericUpDown();
            yearBox.Minimum = 1950;
            yearBox.Maximum = 2100;
            yearBox.Value = Math.Max(1950, Math.Min(2100, year));
            yearBox.SetBounds(Px(58), Px(14), Px(80), Px(24));
            yearBox.ValueChanged += new EventHandler(Year_Changed);
            Controls.Add(yearBox);

            list = new ListBox();
            list.SetBounds(Px(14), Px(48), Px(412), Px(250));
            list.IntegralHeight = false;
            Controls.Add(list);

            Button del = new Button();
            del.Text = "선택 삭제";
            del.SetBounds(Px(14), Px(304), Px(100), Px(28));
            del.Click += new EventHandler(Del_Click);
            Controls.Add(del);
            Button reset = new Button();
            reset.Text = "이 해 기본값으로";
            reset.SetBounds(Px(120), Px(304), Px(130), Px(28));
            reset.Click += new EventHandler(Reset_Click);
            Controls.Add(reset);

            Label al = new Label();
            al.Text = "공휴일 추가 (임시공휴일 등)";
            al.SetBounds(Px(14), Px(346), Px(300), Px(20));
            Controls.Add(al);
            addDate = new DateTimePicker();
            addDate.Format = DateTimePickerFormat.Short;
            addDate.SetBounds(Px(14), Px(370), Px(120), Px(24));
            Controls.Add(addDate);
            addName = new TextBox();
            addName.Text = "임시공휴일";
            addName.SetBounds(Px(140), Px(370), Px(200), Px(24));
            Controls.Add(addName);
            Button add = new Button();
            add.Text = "추가";
            add.SetBounds(Px(346), Px(369), Px(80), Px(26));
            add.Click += new EventHandler(Add_Click);
            Controls.Add(add);

            Label note = new Label();
            note.Text = "설날·추석·부처님오신날과 대체공휴일은 2050년까지 자동으로 들어갑니다.\n바뀐 날은 삭제하거나 직접 추가하세요.";
            note.ForeColor = Color.Gray;
            note.SetBounds(Px(14), Px(404), Px(412), Px(36));
            Controls.Add(note);

            Button close = new Button();
            close.Text = "닫기";
            close.DialogResult = DialogResult.Cancel;
            close.SetBounds(Px(346), Px(434), Px(80), Px(28));
            Controls.Add(close);
            CancelButton = close;

            addDate.Value = new DateTime((int)yearBox.Value, Math.Max(1, Math.Min(12, DateTime.Today.Month)), 1);
            Fill();
            Skin.Apply(this);
        }

        void Fill()
        {
            int y = (int)yearBox.Value;
            Dictionary<DateTime, string> h = cal.Holidays(y);
            keys = new List<DateTime>(h.Keys);
            keys.Sort();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (DateTime d in keys)
            {
                string mark = cal.HolAdd.ContainsKey(d) ? "   [직접 추가]" : "";
                list.Items.Add(d.ToString("M월 d일 (ddd)") + "   " + h[d] + mark);
            }
            list.EndUpdate();
        }

        void Year_Changed(object sender, EventArgs e)
        {
            int y = (int)yearBox.Value;
            try { addDate.Value = new DateTime(y, addDate.Value.Month, Math.Min(addDate.Value.Day, DateTime.DaysInMonth(y, addDate.Value.Month))); } catch { }
            Fill();
        }

        void Del_Click(object sender, EventArgs e)
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            DateTime d = keys[i];
            if (cal.HolAdd.ContainsKey(d)) cal.HolAdd.Remove(d);
            // 기본 공휴일이 있는 날이면 숨김
            foreach (KeyValuePair<DateTime, string> kv in KHoliday.Builtin(d.Year))
                if (kv.Key == d) { cal.HolDel[d] = true; break; }
            cal.SaveHolidays();
            Fill();
        }

        void Reset_Click(object sender, EventArgs e)
        {
            int y = (int)yearBox.Value;
            List<DateTime> rm = new List<DateTime>();
            foreach (DateTime d in cal.HolAdd.Keys) if (d.Year == y) rm.Add(d);
            foreach (DateTime d in rm) cal.HolAdd.Remove(d);
            rm.Clear();
            foreach (DateTime d in cal.HolDel.Keys) if (d.Year == y) rm.Add(d);
            foreach (DateTime d in rm) cal.HolDel.Remove(d);
            cal.SaveHolidays();
            Fill();
        }

        void Add_Click(object sender, EventArgs e)
        {
            string name = addName.Text.Trim();
            if (name.Length == 0) name = "공휴일";
            DateTime d = addDate.Value.Date;
            cal.HolAdd[d] = name;
            cal.SaveHolidays();
            if (d.Year != (int)yearBox.Value) yearBox.Value = d.Year;
            Fill();
        }
    }

    // ================= 알림 팝업 =================

    public class AlarmPopup : Form
    {
        static List<AlarmPopup> open = new List<AlarmPopup>();
        public DateTime Target;
        public event EventHandler OpenCalendar;

        protected override bool ShowWithoutActivation { get { return true; } }

        public AlarmPopup(string head, string title, string detail, DateTime target, float s)
        {
            Target = target;
            Text = "AHNNOTE 알림";
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            int lines = 1;
            foreach (char c in detail) if (c == '\n') lines++;
            lines = Math.Min(lines, 8);
            int w = (int)(320 * s);
            int detailH = (int)((18 * lines + 4) * s);
            ClientSize = new Size(w, (int)(96 * s) + detailH);

            Label hl = new Label();
            hl.Text = head;
            hl.Font = new Font("Malgun Gothic", 8.5f, FontStyle.Bold);
            hl.ForeColor = Skin.Dark ? Color.FromArgb(96, 180, 255) : Color.FromArgb(0, 103, 192);
            hl.SetBounds((int)(14 * s), (int)(10 * s), w - (int)(28 * s), (int)(18 * s));
            Label tl = new Label();
            tl.Text = title;
            tl.Font = new Font("Malgun Gothic", 11f, FontStyle.Bold);
            tl.AutoEllipsis = true;
            tl.SetBounds((int)(14 * s), (int)(30 * s), w - (int)(28 * s), (int)(24 * s));
            Label dl = new Label();
            dl.Text = detail;
            dl.AutoEllipsis = true;
            dl.SetBounds((int)(14 * s), (int)(56 * s), w - (int)(28 * s), detailH);

            Button openBtn = new Button();
            openBtn.Text = "캘린더 열기";
            openBtn.SetBounds(w - (int)(196 * s), (int)(62 * s) + detailH, (int)(100 * s), (int)(28 * s));
            openBtn.Click += new EventHandler(Open_Click);
            Button ok = new Button();
            ok.Text = "확인";
            ok.SetBounds(w - (int)(90 * s), (int)(62 * s) + detailH, (int)(76 * s), (int)(28 * s));
            ok.Click += new EventHandler(Ok_Click);

            Controls.Add(hl);
            Controls.Add(tl);
            Controls.Add(dl);
            Controls.Add(openBtn);
            Controls.Add(ok);
            Skin.Apply(this);
            hl.ForeColor = Skin.Dark ? Color.FromArgb(96, 180, 255) : Color.FromArgb(0, 103, 192);

            // 화면 오른쪽 아래부터 위로 쌓기
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int yPos = wa.Bottom - Height - (int)(12 * s);
            foreach (AlarmPopup p in open) yPos = Math.Min(yPos, p.Top - Height - (int)(8 * s));
            if (yPos < wa.Top) yPos = wa.Bottom - Height - (int)(12 * s);
            Location = new Point(wa.Right - Width - (int)(12 * s), yPos);
            open.Add(this);
        }

        void Open_Click(object sender, EventArgs e)
        {
            if (OpenCalendar != null) OpenCalendar(this, EventArgs.Empty);
            Close();
        }

        void Ok_Click(object sender, EventArgs e) { Close(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            open.Remove(this);
            base.OnFormClosed(e);
        }
    }
}
