using System.Windows;
using System.Windows.Controls;
using ZwSoft.ZwCAD.ApplicationServices;

namespace cadastre
{
    /// <summary>
    /// Dialogue d'import personnalise : selectionne ce qu'on veut importer.
    /// </summary>
    public class CustomImportDialog : Window
    {
        private CheckBox _chkParcelles, _chkBati, _chkLibelles, _chkLocalisants, _chkSubdivisions;

        public CustomImportDialog()
        {
            Title = "Import personnalise";
            Width = 320;
            Height = 330;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.ToolWindow;

            var grid = new Grid();
            grid.Margin = new Thickness(20);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new Label
            {
                Content = "Elements a importer :",
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            _chkParcelles = new CheckBox
            {
                Content = "Parcelles",
                IsChecked = CadastreOptions.ImporterParcelles,
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(_chkParcelles, 1);
            grid.Children.Add(_chkParcelles);

            _chkBati = new CheckBox
            {
                Content = "Batiments",
                IsChecked = CadastreOptions.ImporterBati,
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(_chkBati, 2);
            grid.Children.Add(_chkBati);

            _chkLibelles = new CheckBox
            {
                Content = "Section & N de Parcelle",
                IsChecked = CadastreOptions.ImporterLibelles,
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(_chkLibelles, 3);
            grid.Children.Add(_chkLibelles);

            _chkLocalisants = new CheckBox
            {
                Content = "Localisants (points d'adresse)",
                IsChecked = CadastreOptions.ImporterLocalisants,
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(_chkLocalisants, 4);
            grid.Children.Add(_chkLocalisants);

            _chkSubdivisions = new CheckBox
            {
                Content = "Subdivisions fiscales",
                IsChecked = CadastreOptions.ImporterSubdivisions,
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(_chkSubdivisions, 5);
            grid.Children.Add(_chkSubdivisions);

            // Boutons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var btnOk = new Button { Content = "OK", Width = 80, Height = 26, Margin = new Thickness(0, 0, 10, 0) };
            btnOk.Click += (s, e) => { SaveAndRun(); };

            var btnCancel = new Button { Content = "Annuler", Width = 80, Height = 26 };
            btnCancel.Click += (s, e) => { Close(); };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 4);
            grid.Children.Add(btnPanel);

            Content = grid;
        }

        private void SaveAndRun()
        {
            CadastreOptions.ImporterParcelles    = _chkParcelles.IsChecked == true;
            CadastreOptions.ImporterBati         = _chkBati.IsChecked == true;
            CadastreOptions.ImporterLibelles     = _chkLibelles.IsChecked == true;
            CadastreOptions.ImporterLocalisants  = _chkLocalisants.IsChecked == true;
            CadastreOptions.ImporterSubdivisions = _chkSubdivisions.IsChecked == true;
            Close();

            Document doc = ZwSoft.ZwCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.SendStringToExecute("_CADASTRE ", false, false, false);
        }
    }
}
