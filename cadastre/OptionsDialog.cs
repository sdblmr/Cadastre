using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace cadastre
{
    public class OptionsDialog : Window
    {
        private CheckBox _chkCreerCalques;
        private TextBox _txtParcelles, _txtBati, _txtLibelles, _txtLocalisants, _txtSubdivisions;
        private TextBox _colParcelles, _colBati, _colLibelles, _colLocalisants, _colSubdivisions;

        public OptionsDialog()
        {
            Title = "Options Cadastre";
            Width = 520;
            Height = 380;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.ToolWindow;

            var grid = new Grid();
            grid.Margin = new Thickness(15);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 10; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Checkbox calques
            _chkCreerCalques = new CheckBox
            {
                Content = "Creer des calques dedies",
                IsChecked = CadastreOptions.CreerCalques,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _chkCreerCalques.Checked += (s, e) => UpdateTextBoxesState();
            _chkCreerCalques.Unchecked += (s, e) => UpdateTextBoxesState();
            Grid.SetRow(_chkCreerCalques, 0);
            Grid.SetColumnSpan(_chkCreerCalques, 2);
            grid.Children.Add(_chkCreerCalques);

            // Entete
            var hdrNom = new Label { Content = "Nom du calque", FontWeight = FontWeights.Bold, Margin = new Thickness(20, 5, 0, 3) };
            Grid.SetRow(hdrNom, 1); Grid.SetColumn(hdrNom, 0);
            grid.Children.Add(hdrNom);
            var hdrCol = new Label { Content = "Idx couleur", FontWeight = FontWeights.Bold, Width = 60, Margin = new Thickness(5, 5, 0, 3) };
            Grid.SetRow(hdrCol, 1); Grid.SetColumn(hdrCol, 1);
            grid.Children.Add(hdrCol);

            // Lignes
            int row = 2;
            (_txtParcelles, _colParcelles) = CreateLayerRow(grid, ref row, "Parcelles", CadastreOptions.CalqueParcelles, CadastreOptions.CouleurParcelles);
            (_txtBati,     _colBati)     = CreateLayerRow(grid, ref row, "Batiments", CadastreOptions.CalqueBati, CadastreOptions.CouleurBati);
            (_txtLibelles, _colLibelles) = CreateLayerRow(grid, ref row, "Section & N", CadastreOptions.CalqueLibelles, CadastreOptions.CouleurLibelles);
            (_txtLocalisants, _colLocalisants)   = CreateLayerRow(grid, ref row, "Localisants", CadastreOptions.CalqueLocalisants, CadastreOptions.CouleurLocalisants);
            (_txtSubdivisions, _colSubdivisions) = CreateLayerRow(grid, ref row, "Subdivisions", CadastreOptions.CalqueSubdivisions, CadastreOptions.CouleurSubdivisions);

            // Boutons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            var btnOk = new Button { Content = "OK", Width = 80, Height = 26, Margin = new Thickness(0, 0, 10, 0) };
            btnOk.Click += (s, e) => { Save(); DialogResult = true; Close(); };
            var btnCancel = new Button { Content = "Annuler", Width = 80, Height = 26 };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(btnPanel, 2);
            grid.Children.Add(btnPanel);

            Content = grid;
            UpdateTextBoxesState();
        }

        private (TextBox txtName, TextBox txtColor) CreateLayerRow(Grid grid, ref int row, string label, string defaultName, short defaultColor)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 3, 0, 3) };
            panel.Children.Add(new Label { Content = label + " :", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            var txt = new TextBox { Text = defaultName, Width = 220, Height = 22 };
            panel.Children.Add(txt);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, 0);
            grid.Children.Add(panel);

            var colBox = new TextBox { Text = defaultColor.ToString(), Width = 45, Height = 22, Margin = new Thickness(10, 3, 0, 3) };
            Grid.SetRow(colBox, row);
            Grid.SetColumn(colBox, 1);
            grid.Children.Add(colBox);

            row++;
            return (txt, colBox);
        }

        private void UpdateTextBoxesState()
        {
            bool enabled = _chkCreerCalques.IsChecked == true;
            _txtParcelles.IsEnabled = enabled; _colParcelles.IsEnabled = enabled;
            _txtBati.IsEnabled = enabled;     _colBati.IsEnabled = enabled;
            _txtLibelles.IsEnabled = enabled; _colLibelles.IsEnabled = enabled;
            _txtLocalisants.IsEnabled = enabled;  _colLocalisants.IsEnabled = enabled;
            _txtSubdivisions.IsEnabled = enabled; _colSubdivisions.IsEnabled = enabled;
        }

        private short ParseColor(string s, short def)
        {
            if (short.TryParse(s, out short v) && v >= 0 && v <= 255) return v;
            return def;
        }

        private void Save()
        {
            CadastreOptions.CreerCalques = _chkCreerCalques.IsChecked == true;
            CadastreOptions.CalqueParcelles    = _txtParcelles.Text.Trim();
            CadastreOptions.CalqueBati         = _txtBati.Text.Trim();
            CadastreOptions.CalqueLibelles     = _txtLibelles.Text.Trim();
            CadastreOptions.CalqueLocalisants  = _txtLocalisants.Text.Trim();
            CadastreOptions.CalqueSubdivisions = _txtSubdivisions.Text.Trim();

            CadastreOptions.CouleurParcelles    = ParseColor(_colParcelles.Text.Trim(), 7);
            CadastreOptions.CouleurBati         = ParseColor(_colBati.Text.Trim(), 40);
            CadastreOptions.CouleurLibelles     = ParseColor(_colLibelles.Text.Trim(), 4);
            CadastreOptions.CouleurLocalisants  = ParseColor(_colLocalisants.Text.Trim(), 2);
            CadastreOptions.CouleurSubdivisions = ParseColor(_colSubdivisions.Text.Trim(), 6);
        }
    }
}
