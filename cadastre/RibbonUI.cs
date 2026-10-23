using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Ribbon;
using ZwSoft.Windows;

[assembly: ExtensionApplication(typeof(cadastre.RibbonUI))]

namespace cadastre
{
    public class RibbonUI : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                RibbonControl rc = ComponentManager.Ribbon;
                if (rc == null)
                {
                    Application.Idle += OnIdle;
                }
                else
                {
                    BuildRibbon(rc);
                }
            }
            catch (System.Exception ex)
            {
                LogError("Initialize", ex);
            }
        }

        public void Terminate() { }

        private void OnIdle(object sender, EventArgs e)
        {
            Application.Idle -= OnIdle;
            try
            {
                BuildRibbon(ComponentManager.Ribbon);
            }
            catch (System.Exception ex)
            {
                LogError("OnIdle/BuildRibbon", ex);
            }
        }

        private void BuildRibbon(RibbonControl rc)
        {
            if (rc.FindTab("CADASTRE_TAB_ID") != null) return;

            RibbonTab tab = new RibbonTab
            {
                Title = "Cadastre",
                Id = "CADASTRE_TAB_ID"
            };
            rc.Tabs.Add(tab);

            // Panel principal
            RibbonPanelSource panelSource = new RibbonPanelSource { Title = "Import" };
            RibbonPanel panel = new RibbonPanel { Source = panelSource };
            tab.Panels.Add(panel);

            var icon32 = LoadImage("icon_32.png");
            var icon16 = LoadImage("icon_16.png");

            // ---- 1. Bouton IMPORT (large) ----
            RibbonButton btnImport = new RibbonButton
            {
                Text = "IMPORT",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandHandler = new RibbonCommandHandler("_CADASTRE "),
                LargeImage = icon32,
                Image = icon16
            };
            panelSource.Items.Add(btnImport);

            // ---- 2. Parcelles ----
            RibbonButton btnParcelles = new RibbonButton
            {
                Text = "Parcelles",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = LoadImage("icon_parcelles.png"),
                CommandHandler = new ModeCommandHandler(
                    () => { CadastreOptions.ImporterParcelles = true;
                            CadastreOptions.ImporterBati = false;
                            CadastreOptions.ImporterLibelles = false;
                            CadastreOptions.ImporterLocalisants = false;
                            CadastreOptions.ImporterSubdivisions = false; },
                    "_CADASTRE ")
            };
            panelSource.Items.Add(btnParcelles);

            // ---- 3. Batiments ----
            RibbonButton btnBatiments = new RibbonButton
            {
                Text = "Batiments",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = LoadImage("icon_bati.png"),
                CommandHandler = new ModeCommandHandler(
                    () => { CadastreOptions.ImporterParcelles = false;
                            CadastreOptions.ImporterBati = true;
                            CadastreOptions.ImporterLibelles = false;
                            CadastreOptions.ImporterLocalisants = false;
                            CadastreOptions.ImporterSubdivisions = false; },
                    "_CADASTRE ")
            };
            panelSource.Items.Add(btnBatiments);

            // ---- 4. Libelles ----
            RibbonButton btnLibelles = new RibbonButton
            {
                Text = "Libelles",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = LoadImage("icon_libelles.png"),
                CommandHandler = new ModeCommandHandler(
                    () => { CadastreOptions.ImporterParcelles = false;
                            CadastreOptions.ImporterBati = false;
                            CadastreOptions.ImporterLibelles = true;
                            CadastreOptions.ImporterLocalisants = false;
                            CadastreOptions.ImporterSubdivisions = false; },
                    "_CADASTRE ")
            };
            panelSource.Items.Add(btnLibelles);

            // ---- 5. Import personnalise ----
            RibbonButton btnCustom = new RibbonButton
            {
                Text = "Import\npersonnalise",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Image = LoadImage("icon_custom.png"),
                CommandHandler = new DialogCommandHandler(() => new CustomImportDialog().ShowDialog())
            };
            panelSource.Items.Add(btnCustom);

            // ---- 6. Options ----
            RibbonButton btnOptions = new RibbonButton
            {
                Text = "Options",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Image = LoadImage("icon_options.png"),
                CommandHandler = new DialogCommandHandler(() => new OptionsDialog().ShowDialog())
            };
            panelSource.Items.Add(btnOptions);

            rc.ActiveTab = tab;
        }

        // Conversion GDI -> WPF : la doc officielle ZWCAD exige ce chemin
        // (System.Drawing.Bitmap -> CreateBitmapSourceFromHBitmap).
        // Charger un BitmapImage directement depuis un PNG ne fonctionne pas
        // pour LargeImage dans le ruban ZWCAD.
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private System.Windows.Media.Imaging.BitmapSource LoadImage(string fileName)
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(dllDir ?? "", "Resources", fileName);

                if (!File.Exists(path))
                {
                    LogMessage($"[Cadastre] Icone introuvable : {path}");
                    return null;
                }

                // Charger en System.Drawing.Bitmap (GDI+), puis convertir en WPF
                using (var bmp = new System.Drawing.Bitmap(path))
                {
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        LogMessage($"[Cadastre] Icone chargee : {fileName} ({bmp.Width}x{bmp.Height})");
                        return source;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch (System.Exception ex)
            {
                LogError($"LoadImage({fileName})", ex);
                return null;
            }
        }

        private void LogError(string context, System.Exception ex)
        {
            LogMessage($"[Cadastre] Erreur dans {context} : {ex}");
        }

        private void LogMessage(string message)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n" + message);
        }
    }

    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        private readonly string _command;

        public RibbonCommandHandler(string command)
        {
            _command = command;
        }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.SendStringToExecute(_command, true, false, true);
        }
    }

    /// <summary>
    /// Handler pour les boutons de mode (Parcelles, Batiments, Libelles).
    /// Configure les options avant de lancer la commande.
    /// </summary>
    public class ModeCommandHandler : System.Windows.Input.ICommand
    {
        private readonly System.Action _configure;
        private readonly string _command;

        public ModeCommandHandler(System.Action configure, string command)
        {
            _configure = configure;
            _command = command;
        }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            _configure();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.SendStringToExecute(_command, true, false, true);
        }
    }

    /// <summary>
    /// Handler pour les boutons qui ouvrent un dialogue WPF.
    /// </summary>
    public class DialogCommandHandler : System.Windows.Input.ICommand
    {
        private readonly System.Action _openDialog;

        public DialogCommandHandler(System.Action openDialog)
        {
            _openDialog = openDialog;
        }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            _openDialog();
        }
    }
}
