using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.ViewModels;
using EndFieldFightHelper.Views;
using System;
using System.Collections.Generic;

namespace EndFieldFightHelper;

public class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _viewMap = new()
    {
        [typeof(HomePageViewModel)] = () => new HomePageView(),
        [typeof(SettingsPageViewModel)] = () => new SettingsPageView(),
    };

    private readonly Dictionary<object, Control> _controlCache = [];

    public Control Build(object? param)
    {
        if (param is null)
            return new TextBlock { Text = "Data is null." };

        if (_controlCache.TryGetValue(param, out var cached))
            return cached;

        var type = param.GetType();
        if (_viewMap.TryGetValue(type, out var factory))
        {
            var view = factory();
            view.DataContext = param;
            _controlCache[param] = view;
            return view;
        }

        return new TextBlock { Text = $"No view for {type.Name}." };
    }

    public bool Match(object? data) => data is ObservableObject;
}
