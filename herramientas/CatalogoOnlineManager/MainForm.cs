using CatalogoOnlineManager.Servicios;

namespace CatalogoOnlineManager;

public sealed class MainForm : Form
{
    private RepoSitio? _repo;
    private CancellationTokenSource? _cts;
    private bool _ocupado;

    private readonly Label _lblRaiz = new();
    private readonly Button _btnCambiarRaiz = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _log = new();
    private readonly Button _btnCancelar = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    // Imágenes
    private readonly CheckedListBox _lstCarpetas = new();
    private readonly CheckBox _chkForce = new();
    private readonly NumericUpDown _numHilos = new();
    private readonly Button _btnOptimizar = new();
    private readonly Button _btnTodasCarpetas = new();
    private readonly Button _btnNingunaCarpeta = new();
    private readonly Button _btnRefrescarImg = new();

    // Productos
    private readonly CheckedListBox _lstFuentes = new();
    private readonly CheckBox _chkUsarSeleccion = new();
    private readonly CheckBox _chkActualizarExtra = new();
    private readonly TextBox _txtCsvActualizar = new();
    private readonly Button _btnElegirCsvActualizar = new();
    private readonly Button _btnGenerar = new();
    private readonly Button _btnRefrescarProd = new();

    // Importar
    private readonly TextBox _txtZip = new();
    private readonly Button _btnElegirZip = new();
    private readonly ComboBox _cmbCarpetaZip = new();
    private readonly Button _btnImportar = new();

    private readonly OptimizadorImagenes _optimizador = new();
    private readonly GeneradorCatalogo _generador = new();
    private readonly ImportadorZipLethorPos _importador = new();

    public MainForm()
    {
        Text = "Catálogo en línea — LethorPos / pc2x";
        Width = 960;
        Height = 720;
        MinimumSize = new Size(800, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        ConstruirUi();
        try
        {
            _repo = new RepoSitio();
            ActualizarRaizUi();
            RefrescarListas();
        }
        catch (Exception ex)
        {
            _repo = null;
            _lblRaiz.Text = "Raíz: (no detectada)";
            AppendLog(ex.Message);
            SetEstado(ex.Message, error: true);
        }
    }

    private void ConstruirUi()
    {
        var top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 8, 8, 4)
        };
        _lblRaiz.AutoSize = false;
        _lblRaiz.Dock = DockStyle.Fill;
        _lblRaiz.TextAlign = ContentAlignment.MiddleLeft;
        _btnCambiarRaiz.Text = "Cambiar carpeta…";
        _btnCambiarRaiz.Dock = DockStyle.Right;
        _btnCambiarRaiz.Width = 140;
        _btnCambiarRaiz.Click += (_, _) => CambiarRaiz();
        top.Controls.Add(_lblRaiz);
        top.Controls.Add(_btnCambiarRaiz);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(12, 6);
        _tabs.TabPages.Add(CrearTabImagenes());
        _tabs.TabPages.Add(CrearTabProductos());
        _tabs.TabPages.Add(CrearTabImportar());

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 220,
            Padding = new Padding(8)
        };
        var logHeader = new Panel { Dock = DockStyle.Top, Height = 32 };
        var lblLog = new Label
        {
            Text = "Log",
            AutoSize = true,
            Location = new Point(0, 6)
        };
        _btnCancelar.Text = "Cancelar";
        _btnCancelar.Enabled = false;
        _btnCancelar.Dock = DockStyle.Right;
        _btnCancelar.Width = 100;
        _btnCancelar.Click += (_, _) => _cts?.Cancel();
        logHeader.Controls.Add(lblLog);
        logHeader.Controls.Add(_btnCancelar);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Both;
        _log.Font = new Font("Consolas", 9F);
        _log.WordWrap = false;
        bottom.Controls.Add(_log);
        bottom.Controls.Add(logHeader);

        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_statusLabel);
        _status.Dock = DockStyle.Bottom;

        Controls.Add(_tabs);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_status);
    }

    private TabPage CrearTabImagenes()
    {
        var page = new TabPage("Imágenes");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _btnRefrescarImg.Text = "Actualizar lista";
        _btnRefrescarImg.AutoSize = true;
        _btnRefrescarImg.Click += (_, _) => RefrescarCarpetas();
        _btnTodasCarpetas.Text = "Todas";
        _btnTodasCarpetas.AutoSize = true;
        _btnTodasCarpetas.Click += (_, _) =>
        {
            for (var i = 0; i < _lstCarpetas.Items.Count; i++)
            {
                _lstCarpetas.SetItemChecked(i, true);
            }
        };
        _btnNingunaCarpeta.Text = "Ninguna";
        _btnNingunaCarpeta.AutoSize = true;
        _btnNingunaCarpeta.Click += (_, _) =>
        {
            for (var i = 0; i < _lstCarpetas.Items.Count; i++)
            {
                _lstCarpetas.SetItemChecked(i, false);
            }
        };
        tools.Controls.AddRange([_btnRefrescarImg, _btnTodasCarpetas, _btnNingunaCarpeta]);

        _lstCarpetas.Dock = DockStyle.Fill;
        _lstCarpetas.CheckOnClick = true;

        var acciones = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _chkForce.Text = "Forzar regeneración";
        _chkForce.AutoSize = true;
        var lblHilos = new Label { Text = "Hilos:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) };
        _numHilos.Minimum = 1;
        _numHilos.Maximum = 64;
        _numHilos.Value = Math.Clamp(Environment.ProcessorCount, 1, 64);
        _numHilos.Width = 60;
        _btnOptimizar.Text = "Optimizar";
        _btnOptimizar.AutoSize = true;
        _btnOptimizar.Click += async (_, _) => await OptimizarAsync();
        acciones.Controls.AddRange([_chkForce, lblHilos, _numHilos, _btnOptimizar]);

        panel.Controls.Add(tools, 0, 0);
        panel.Controls.Add(_lstCarpetas, 0, 1);
        panel.Controls.Add(acciones, 0, 2);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage CrearTabProductos()
    {
        var page = new TabPage("Productos");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _btnRefrescarProd.Text = "Actualizar lista fuente/";
        _btnRefrescarProd.AutoSize = true;
        _btnRefrescarProd.Click += (_, _) => RefrescarFuentes();
        _chkUsarSeleccion.Text = "Solo archivos seleccionados (si no, toda fuente/)";
        _chkUsarSeleccion.AutoSize = true;
        tools.Controls.AddRange([_btnRefrescarProd, _chkUsarSeleccion]);

        _lstFuentes.Dock = DockStyle.Fill;
        _lstFuentes.CheckOnClick = true;

        var actualizar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true
        };
        actualizar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actualizar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actualizar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _chkActualizarExtra.Text = "Actualizar con CSV extra";
        _chkActualizarExtra.AutoSize = true;
        _txtCsvActualizar.Dock = DockStyle.Fill;
        _btnElegirCsvActualizar.Text = "…";
        _btnElegirCsvActualizar.Width = 40;
        _btnElegirCsvActualizar.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "CSV/JSON (*.csv;*.json)|*.csv;*.json|Todos|*.*",
                Title = "CSV de actualización"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtCsvActualizar.Text = dlg.FileName;
                _chkActualizarExtra.Checked = true;
            }
        };
        actualizar.Controls.Add(_chkActualizarExtra, 0, 0);
        actualizar.Controls.Add(_txtCsvActualizar, 1, 0);
        actualizar.Controls.Add(_btnElegirCsvActualizar, 2, 0);

        _btnGenerar.Text = "Generar catálogo";
        _btnGenerar.AutoSize = true;
        _btnGenerar.Click += async (_, _) => await GenerarAsync();

        panel.Controls.Add(tools, 0, 0);
        panel.Controls.Add(_lstFuentes, 0, 1);
        panel.Controls.Add(actualizar, 0, 2);
        panel.Controls.Add(_btnGenerar, 0, 3);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage CrearTabImportar()
    {
        var page = new TabPage("Importar ZIP");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ayuda = new Label
        {
            Text =
                "Importa un ZIP exportado desde LethorPos (Exportar → Catálogo en línea).\n" +
                "Copia imágenes a img\\{carpeta}\\full\\, regenera opt/ y actualiza el catálogo de productos.",
            AutoSize = true,
            MaximumSize = new Size(900, 0)
        };

        var zipRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true
        };
        zipRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        zipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        zipRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lblZip = new Label { Text = "ZIP:", AutoSize = true, Anchor = AnchorStyles.Left };
        _txtZip.Dock = DockStyle.Fill;
        _btnElegirZip.Text = "…";
        _btnElegirZip.Width = 40;
        _btnElegirZip.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "ZIP (*.zip)|*.zip|Todos|*.*",
                Title = "ZIP de catálogo en línea"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtZip.Text = dlg.FileName;
            }
        };
        zipRow.Controls.Add(lblZip, 0, 0);
        zipRow.Controls.Add(_txtZip, 1, 0);
        zipRow.Controls.Add(_btnElegirZip, 2, 0);

        var destRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        destRow.Controls.Add(new Label { Text = "Carpeta img/:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        _cmbCarpetaZip.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbCarpetaZip.Width = 220;
        destRow.Controls.Add(_cmbCarpetaZip);
        _btnImportar.Text = "Importar al catálogo en línea";
        _btnImportar.AutoSize = true;
        _btnImportar.Click += async (_, _) => await ImportarZipAsync();
        destRow.Controls.Add(_btnImportar);

        panel.Controls.Add(ayuda, 0, 0);
        panel.Controls.Add(zipRow, 0, 1);
        panel.Controls.Add(destRow, 0, 2);
        page.Controls.Add(panel);
        return page;
    }

    private void CambiarRaiz()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Selecciona la raíz del sitio (debe contener img/ y productos/)",
            UseDescriptionForTitle = true
        };
        if (_repo is not null)
        {
            dlg.SelectedPath = _repo.Raiz;
        }

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (_repo is null)
            {
                _repo = new RepoSitio(dlg.SelectedPath);
            }
            else
            {
                _repo.CambiarRaiz(dlg.SelectedPath);
            }

            ActualizarRaizUi();
            RefrescarListas();
            AppendLog($"Raíz: {_repo.Raiz}");
            SetEstado("Raíz actualizada", error: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ActualizarRaizUi()
    {
        _lblRaiz.Text = _repo is null ? "Raíz: (no detectada)" : $"Raíz: {_repo.Raiz}";
    }

    private void RefrescarListas()
    {
        RefrescarCarpetas();
        RefrescarFuentes();
    }

    private void RefrescarCarpetas()
    {
        _lstCarpetas.Items.Clear();
        _cmbCarpetaZip.Items.Clear();
        if (_repo is null)
        {
            return;
        }

        foreach (var nombre in _repo.ListarCarpetasImagen())
        {
            _lstCarpetas.Items.Add(nombre);
            _cmbCarpetaZip.Items.Add(nombre);
        }

        if (_cmbCarpetaZip.Items.Contains("General"))
        {
            _cmbCarpetaZip.SelectedItem = "General";
        }
        else if (_cmbCarpetaZip.Items.Count > 0)
        {
            _cmbCarpetaZip.SelectedIndex = 0;
        }
        else
        {
            _cmbCarpetaZip.Text = "General";
        }
    }

    private void RefrescarFuentes()
    {
        _lstFuentes.Items.Clear();
        if (_repo is null)
        {
            return;
        }

        foreach (var ruta in _repo.ListarFuentes())
        {
            _lstFuentes.Items.Add(ruta, false);
        }
    }

    private async Task OptimizarAsync()
    {
        if (!PrepararTrabajo())
        {
            return;
        }

        var force = _chkForce.Checked;
        var threads = (int)_numHilos.Value;
        var seleccion = _lstCarpetas.CheckedItems.Cast<string>().ToList();
        // Ninguna marcada = todas (igual que el CLI sin args).
        IReadOnlyList<string>? carpetas = seleccion.Count > 0 ? seleccion : null;

        await EjecutarAsync(async (ct, log) =>
        {
            var codigo = await _optimizador.OptimizarAsync(_repo!.ImgDir, carpetas, force, threads, log, ct);
            return codigo == 0;
        }, "Optimización de imágenes");
    }

    private async Task GenerarAsync()
    {
        if (!PrepararTrabajo())
        {
            return;
        }

        List<string>? basePaths = null;
        if (_chkUsarSeleccion.Checked)
        {
            basePaths = _lstFuentes.CheckedItems.Cast<string>().ToList();
            if (basePaths.Count == 0)
            {
                MessageBox.Show(this, "Selecciona al menos un archivo de fuente/ o desmarca «Solo archivos seleccionados».", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        List<string>? actualizar = null;
        if (_chkActualizarExtra.Checked)
        {
            var csv = _txtCsvActualizar.Text.Trim();
            if (csv.Length == 0 || !File.Exists(csv))
            {
                MessageBox.Show(this, "Indica un CSV/JSON válido para actualizar.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            actualizar = [csv];
        }

        await EjecutarAsync(async (ct, log) =>
        {
            var codigo = await _generador.GenerarAsync(
                _repo!.ProductosDir,
                _repo.ImgDir,
                basePaths,
                actualizar,
                log,
                ct);
            return codigo == 0;
        }, "Generación de catálogo");
    }

    private async Task ImportarZipAsync()
    {
        if (!PrepararTrabajo())
        {
            return;
        }

        var zip = _txtZip.Text.Trim();
        if (zip.Length == 0 || !File.Exists(zip))
        {
            MessageBox.Show(this, "Elige un ZIP válido exportado desde LethorPos.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var carpeta = (_cmbCarpetaZip.Text ?? "").Trim();
        if (carpeta.Length == 0)
        {
            carpeta = "General";
        }

        var threads = (int)_numHilos.Value;
        await EjecutarAsync(async (ct, log) =>
        {
            var codigo = await _importador.ImportarAsync(_repo!, zip, carpeta, threads, log, ct);
            return codigo == 0;
        }, "Importación ZIP");
    }

    private bool PrepararTrabajo()
    {
        if (_ocupado)
        {
            return false;
        }

        if (_repo is null)
        {
            MessageBox.Show(this, "Define la raíz del sitio (img/ + productos/).", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private async Task EjecutarAsync(Func<CancellationToken, Action<string>, Task<bool>> trabajo, string nombre)
    {
        _ocupado = true;
        _cts = new CancellationTokenSource();
        SetControlesHabilitados(false);
        _btnCancelar.Enabled = true;
        _log.Clear();
        AppendLog($"=== {nombre} ===");
        SetEstado($"{nombre}…", error: false);

        try
        {
            var ok = await Task.Run(() => trabajo(_cts.Token, AppendLogSafe), _cts.Token);
            if (_cts.IsCancellationRequested)
            {
                AppendLog("Cancelado.");
                SetEstado("Cancelado", error: true);
            }
            else if (ok)
            {
                AppendLog("OK.");
                SetEstado($"{nombre}: OK", error: false);
            }
            else
            {
                AppendLog("Terminó con errores.");
                SetEstado($"{nombre}: error", error: true);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelado.");
            SetEstado("Cancelado", error: true);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            SetEstado($"{nombre}: error", error: true);
        }
        finally
        {
            _ocupado = false;
            _btnCancelar.Enabled = false;
            _cts.Dispose();
            _cts = null;
            SetControlesHabilitados(true);
            RefrescarListas();
        }
    }

    private void SetControlesHabilitados(bool enabled)
    {
        _btnCambiarRaiz.Enabled = enabled;
        _btnOptimizar.Enabled = enabled;
        _btnGenerar.Enabled = enabled;
        _btnImportar.Enabled = enabled;
        _btnRefrescarImg.Enabled = enabled;
        _btnRefrescarProd.Enabled = enabled;
        _tabs.Enabled = enabled;
    }

    private void AppendLogSafe(string linea)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(linea));
            return;
        }

        AppendLog(linea);
    }

    private void AppendLog(string linea)
    {
        _log.AppendText(linea + Environment.NewLine);
    }

    private void SetEstado(string texto, bool error)
    {
        _statusLabel.Text = texto;
        _statusLabel.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }
}
