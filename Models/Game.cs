// OptiScaler Client - A frontend for managing OptiScaler installations
// Copyright (C) 2026 Agustín Montaña (Agustinm28)
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OptiscalerClient.Models;

public enum GamePlatform
{
    Steam,
    Epic,
    GOG,
    Xbox,
    EA,
    BattleNet,
    Ubisoft,
    Manual,
    Custom
}

public class Game : INotifyPropertyChanged
{
    private string _name = string.Empty;
    public string Name { get => _name; set => SetField(ref _name, value); }

    private string _installPath = string.Empty;
    public string InstallPath { get => _installPath; set => SetField(ref _installPath, value); }

    public GamePlatform Platform { get; set; }
    public bool IsManual => Platform == GamePlatform.Manual;
    public string AppId { get; set; } = string.Empty; 
    public string ExecutablePath { get; set; } = string.Empty; 

    private string? _coverImageUrl;
    public string? CoverImageUrl { get => _coverImageUrl; set => SetField(ref _coverImageUrl, value); }

    // Detected Technologies (Reactive)
    private string? _dlssVersion;
    public string? DlssVersion { get => _dlssVersion; set => SetField(ref _dlssVersion, value); }
    public string? DlssPath { get; set; }

    private string? _dlssFrameGenVersion;
    public string? DlssFrameGenVersion { get => _dlssFrameGenVersion; set => SetField(ref _dlssFrameGenVersion, value); }
    public string? DlssFrameGenPath { get; set; }

    private string? _fsrVersion;
    public string? FsrVersion { get => _fsrVersion; set => SetField(ref _fsrVersion, value); }
    public string? FsrPath { get; set; }

    private string? _xessVersion;
    public string? XessVersion { get => _xessVersion; set => SetField(ref _xessVersion, value); }
    public string? XessPath { get; set; }

    private bool _isOptiscalerInstalled;
    public bool IsOptiscalerInstalled { get => _isOptiscalerInstalled; set => SetField(ref _isOptiscalerInstalled, value); }

    private string? _optiscalerVersion;
    public string? OptiscalerVersion { get => _optiscalerVersion; set => SetField(ref _optiscalerVersion, value); }

    private string? _fsr4ExtraVersion;
    public string? Fsr4ExtraVersion { get => _fsr4ExtraVersion; set => SetField(ref _fsr4ExtraVersion, value); }

    private bool _isCompatibleWithOptiscaler;
    public bool IsCompatibleWithOptiscaler { get => _isCompatibleWithOptiscaler; set => SetField(ref _isCompatibleWithOptiscaler, value); }

    // INotifyPropertyChanged Implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
