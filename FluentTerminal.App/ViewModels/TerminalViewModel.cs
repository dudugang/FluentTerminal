﻿using FluentTerminal.App.Services;
using FluentTerminal.App.Views;
using FluentTerminal.Models;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace FluentTerminal.App.ViewModels
{
    public class TerminalViewModel : ViewModelBase
    {
        private const string DefaultTitle = "Fluent Terminal";
        private readonly IDialogService _dialogService;
        private readonly ISettingsService _settingsService;
        private readonly ITerminalService _terminalService;
        private CoreDispatcher _dispatcher;
        private string _resizeOverlayContent;
        private DispatcherTimer _resizeOverlayTimer;
        private bool _showResizeOverlay;
        private string _startupDirectory;
        private int _terminalId;
        private ITerminalView _terminalView;
        private string _title;

        public TerminalViewModel(int id, ISettingsService settingsService, ITerminalService terminalService, IDialogService dialogService, string startupDirectory)
        {
            Id = id;
            Title = DefaultTitle;

            _settingsService = settingsService;
            _settingsService.CurrentThemeChanged += OnCurrentThemeChanged;
            _settingsService.TerminalOptionsChanged += OnTerminalOptionsChanged;
            _terminalService = terminalService;
            _dialogService = dialogService;
            _startupDirectory = startupDirectory;
            _resizeOverlayTimer = new DispatcherTimer();
            _resizeOverlayTimer.Interval = new TimeSpan(0, 0, 2);
            _resizeOverlayTimer.Tick += OnResizeOverlayTimerFinished;

            CloseCommand = new RelayCommand(InvokeCloseRequested);

            _dispatcher = CoreApplication.GetCurrentView().Dispatcher;
        }

        public event EventHandler CloseRequested;

        public event EventHandler<string> TitleChanged;

        public RelayCommand CloseCommand { get; }

        public int Id { get; private set; }

        public bool Initialized { get; private set; }

        public string ResizeOverlayContent
        {
            get => _resizeOverlayContent;
            set => Set(ref _resizeOverlayContent, value);
        }

        public bool ShowResizeOverlay
        {
            get => _showResizeOverlay;
            set
            {
                Set(ref _showResizeOverlay, value);
                if (value)
                {
                    if (_resizeOverlayTimer.IsEnabled)
                    {
                        _resizeOverlayTimer.Stop();
                    }
                    _resizeOverlayTimer.Start();
                }
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                value = string.IsNullOrWhiteSpace(value) ? DefaultTitle : value;

                if (Set(ref _title, value))
                {
                    TitleChanged?.Invoke(this, Title);
                }
            }
        }

        public void CloseView()
        {
            _terminalView.Close();
        }

        public Task FocusTerminal()
        {
            return _terminalView?.FocusTerminal();
        }

        public async Task OnViewIsReady(ITerminalView terminalView)
        {
            _terminalView = terminalView;

            var options = _settingsService.GetTerminalOptions();
            var theme = _settingsService.GetCurrentTheme();

            var size = await _terminalView.CreateTerminal(options, theme.Colors);
            var configuration = _settingsService.GetShellConfiguration();

            if (!string.IsNullOrWhiteSpace(_startupDirectory))
            {
                configuration.WorkingDirectory = _startupDirectory;
            }

            var response = await _terminalService.CreateTerminal(size, configuration);

            if (response.Success)
            {
                _terminalId = response.Id;
                _terminalView.TerminalSizeChanged += OnTerminalSizeChanged;
                _terminalView.TerminalTitleChanged += OnTerminalTitleChanged;

                await _terminalView.ConnectToSocket(response.WebSocketUrl);
                Initialized = true;
            }
            else
            {
                await _dialogService.ShowDialogAsnyc("Error", response.Error, DialogButton.OK);
            }

            await FocusTerminal();
        }

        private void InvokeCloseRequested()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void OnCurrentThemeChanged(object sender, EventArgs e)
        {
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                var currentTheme = _settingsService.GetCurrentTheme();
                await _terminalView.ChangeTheme(currentTheme.Colors);
            });
        }

        private void OnResizeOverlayTimerFinished(object sender, object e)
        {
            _resizeOverlayTimer.Stop();
            ShowResizeOverlay = false;
        }

        private async void OnTerminalOptionsChanged(object sender, EventArgs e)
        {
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                var options = _settingsService.GetTerminalOptions();
                await _terminalView.ChangeOptions(options);
            });
        }

        private async void OnTerminalSizeChanged(object sender, TerminalSize e)
        {
            if (!Initialized)
            {
                return;
            }
            ResizeOverlayContent = $"{e.Columns} x {e.Rows}";
            ShowResizeOverlay = true;
            await _terminalService.ResizeTerminal(_terminalId, e);
        }

        private void OnTerminalTitleChanged(object sender, string e)
        {
            Title = e;
        }
    }
}