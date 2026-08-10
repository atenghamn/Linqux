using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Linqux.LinqEngine;

namespace Linqux.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _connectionString = "";

    [ObservableProperty]
    private bool _useInteractiveAuth;

    [ObservableProperty]
    private string _linqQuery = "db.Something.Take(10)";

    [ObservableProperty]
    private IReadOnlyList<object>? _queryResult;

    [ObservableProperty]
    private string? _generatedSql;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private bool _isScaffolding;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _forceRescaffold;

    [ObservableProperty]
    private string? _selectedConfigName;

    [ObservableProperty]
    private string _configName = "";

    public ObservableCollection<string> SavedConfigs { get; } = new();

    private ScaffoldedModel? _model;

    public MainWindowViewModel()
    {
        LoadSavedConfigs();
    }

    private void LoadSavedConfigs()
    {
        SavedConfigs.Clear();
        foreach (var connection in ConnectionConfigStore.Load().OrderBy(c => c.Name))
        {
            SavedConfigs.Add(connection.Name);
        }
    }

    partial void OnSelectedConfigNameChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        var connection = ConnectionConfigStore.Load().FirstOrDefault(c => c.Name == value);
        if (connection != null)
        {
            ConnectionString = connection.ConnectionString;
            ConfigName = connection.Name;
        }
    }

    private bool CanConnect() => !IsScaffolding && !string.IsNullOrWhiteSpace(ConnectionString);

    private string EffectiveConnectionString => UseInteractiveAuth
        ? ConnectionStringBuilder.EnableInteractiveAuth(ConnectionString.Trim())
        : ConnectionString.Trim();

    private bool CanExecuteQuery() => IsConnected && !IsScaffolding && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        IsScaffolding = true;
        IsConnected = false;
        _model = null;
        ErrorMessage = null;
        QueryResult = null;
        GeneratedSql = null;

        try
        {
            if (UseInteractiveAuth || ConnectionStringBuilder.UsesInteractiveAuth(ConnectionString))
            {
                ConnectionStatus = "Sign-in required - a browser tab/window has opened. Complete the sign-in there...";
                await AzureAuth.SignInInteractiveAsync(ConnectionString);
            }

            ConnectionStatus = "Scaffolding models...";
            _model = await ScaffoldingService.ScaffoldAsync(EffectiveConnectionString, ForceRescaffold);
            IsConnected = true;
            ConnectionStatus = $"Connected — {_model.EntityCount} entities scaffolded";
        }
        catch (Exception ex)
        {
            ConnectionStatus = "Connection failed";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsScaffolding = false;
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        var name = string.IsNullOrWhiteSpace(ConfigName)
            ? DeriveName(ConnectionString)
            : ConfigName.Trim();

        var connections = ConnectionConfigStore.Load();
        connections.RemoveAll(c => c.Name == name);
        connections.Add(new SavedConnection(name, ConnectionString.Trim()));
        ConnectionConfigStore.Save(connections);

        LoadSavedConfigs();
        SelectedConfigName = name;
        ConfigName = name;
    }

    [RelayCommand]
    private void RemoveConfig()
    {
        if (string.IsNullOrWhiteSpace(SelectedConfigName)) return;

        ConnectionConfigStore.Delete(SelectedConfigName);
        LoadSavedConfigs();
        SelectedConfigName = null;
        ConfigName = "";
    }

    private static string DeriveName(string connectionString)
    {
        foreach (var part in connectionString.Split(';'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Database", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return "Connection " + DateTime.Now.ToString("HH:mm");
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteQueryAsync()
    {
        if (_model == null || string.IsNullOrWhiteSpace(LinqQuery)) return;

        IsBusy = true;
        ErrorMessage = null;
        QueryResult = null;
        GeneratedSql = null;

        try
        {
            var result = await LinqEngine.Engine.ExecuteQueryAsync(_model, EffectiveConnectionString, LinqQuery);

            if (result.ErrorMessage != null)
            {
                ErrorMessage = result.ErrorMessage;
            }
            else
            {
                QueryResult = result.Data;
                GeneratedSql = result.GeneratedSql;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
