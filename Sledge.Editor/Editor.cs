﻿using System;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Mediator;
using Sledge.DataStructures.MapObjects;
using Sledge.Editor.Brushes;
using Sledge.Editor.Documents;
using Sledge.Editor.Settings;
using Sledge.Editor.UI;
using Sledge.Editor.Visgroups;
using Sledge.Providers;
using Sledge.Providers.GameData;
using Sledge.Providers.Map;
using Sledge.Database;
using Sledge.Editor.Tools;
using Sledge.Providers.Texture;
using Sledge.Settings;
using Hotkeys = Sledge.Editor.UI.Hotkeys;

namespace Sledge.Editor
{
    public partial class Editor : Form, IMediatorListener
    {
        public static Editor Instance { get; private set; }

        public bool CaptureAltPresses { get; set; }

        public Editor()
        {
            InitializeComponent();
            tsbNew.Click += (sender, e) => NewFile();
            tsbOpen.Click += (sender, e) => OpenFile();
            tsbSave.Click += (sender, e) => SaveFile();
            Instance = this;
        }

        public void SelectTool(BaseTool t)
        {
            ToolManager.Activate(t);
            foreach (var tsb in from object item in tspTools.Items select ((ToolStripButton) item))
            {
                tsb.Checked = (tsb.Name == ToolManager.ActiveTool.GetName());
            }
        }

        public static void NewFile()
        {
            using (var gsd = new GameSelectionForm())
            {
                gsd.ShowDialog();
                if (gsd.SelectedGameID < 0) return;
                var game = Context.DBContext.GetAllGames().Single(g => g.ID == gsd.SelectedGameID);
                DocumentManager.AddAndSwitch(new Document(null, new Map(), game));
            }
        }

        private static void OpenFile()
        {
            using (var ofd = new OpenFileDialog())
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                using (var gsd = new GameSelectionForm())
                {
                    gsd.ShowDialog();
                    if (gsd.SelectedGameID < 0) return;
                    var game = Context.DBContext.GetAllGames().Single(g => g.ID == gsd.SelectedGameID);
                    var filename = ofd.FileName;
                    try
                    {
                        var map = MapProvider.GetMapFromFile(filename);
                        DocumentManager.AddAndSwitch(new Document(filename, map, game));
                    }
                    catch (ProviderException e)
                    {
                        Error.Warning("The map file could not be opened:\n" + e.Message);
                    }
                }
            }
        }

        private static void SaveFile()
        {
            Mediator.Publish(HotkeysMediator.FileSave);
        }

        private void EditorLoad(object sender, EventArgs e)
        {
            ViewportManager.Init(tblQuadView);
            ToolManager.Init();
            BrushManager.Init();
            BrushManager.SetBrushControl(BrushCreatePanel);
            VisgroupManager.SetVisgroupPanel(VisgroupsPanel);

            foreach (var tool in ToolManager.Tools)
            {
                var tl = tool;
                tspTools.Items.Add(new ToolStripButton(
                    "",
                    tl.GetIcon(),
                    (s, ea) => SelectTool(tl),
                    tl.GetName())
                        {
                            Checked = (tl == ToolManager.ActiveTool)
                        }
                    );
            }

            MapProvider.Register(new RmfProvider());
            MapProvider.Register(new MapFormatProvider());
            MapProvider.Register(new VmfProvider());
            GameDataProvider.Register(new FgdProvider());
            TextureProvider.Register(new WadProvider());

            Subscribe();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Suppress presses of the alt key if required
            if (CaptureAltPresses && (keyData & Keys.Alt) == Keys.Alt)
            {
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        private void OpenSettingsDialog(object sender, EventArgs e)
        {
            using (var sf = new SettingsForm())
            {
                sf.ShowDialog();
            }
        }

        private void CompileMapClicked(object sender, EventArgs e)
        {
            Mediator.Publish(HotkeysMediator.FileCompile);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return Hotkeys.HotkeyDown(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }

        public void Notify(string message, object data)
        {
            Mediator.ExecuteDefault(this, message, data);
        }

        private void Subscribe()
        {
            Mediator.Subscribe(HotkeysMediator.FourViewAutosize, this);
            Mediator.Subscribe(HotkeysMediator.FourViewFocusBottomLeft, this);
            Mediator.Subscribe(HotkeysMediator.FourViewFocusBottomRight, this);
            Mediator.Subscribe(HotkeysMediator.FourViewFocusTopLeft, this);
            Mediator.Subscribe(HotkeysMediator.FourViewFocusTopRight, this);
        }

        public void FourViewAutosize()
        {
            tblQuadView.ResetViews();
        }

        public void FourViewFocusTopLeft()
        {
            tblQuadView.FocusOn(0, 0);
        }

        public void FourViewFocusTopRight()
        {
            tblQuadView.FocusOn(0, 1);
        }

        public void FourViewFocusBottomLeft()
        {
            tblQuadView.FocusOn(1, 0);
        }

        public void FourViewFocusBottomRight()
        {
            tblQuadView.FocusOn(1, 1);
        }
    }
}