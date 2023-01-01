using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Text_Grab.Controls;
using Text_Grab.Models;
using Text_Grab.Properties;

namespace Text_Grab.Utilities;

public class CustomBottomBarUtilities
{
    // this method takes a json string and returns a list of CustomButton using system.text.json
    public static List<ButtonInfo> GetCustomBottomBarItemsSetting()
    {
        string json = Settings.Default.BottomButtonsJson;

        if (string.IsNullOrWhiteSpace(json))
            return ButtonInfo.DefaultButtonList;

        // create a list of custom bottom bar items
        List<ButtonInfo>? customBottomBarItems = new();

        // deserialize the json string into a list of custom bottom bar items
        customBottomBarItems = JsonSerializer.Deserialize<List<ButtonInfo>>(json);

        // return the list of custom bottom bar items
        if (customBottomBarItems is null)
            return ButtonInfo.DefaultButtonList;

        return customBottomBarItems;
    }

    // a method to save a list of collapsible buttons to the settings as json
    public static void SaveCustomBottomBarItemsSetting(List<CollapsibleButton> bottomBarButtons)
    {
        List<ButtonInfo> customButtons = new();

        foreach (CollapsibleButton collapsible in bottomBarButtons)
            customButtons.Add(new(collapsible));

        SaveCustomBottomBarItemsSetting(customButtons);
    }

    public static void SaveCustomBottomBarItemsSetting(List<ButtonInfo> bottomBarButtons)
    {
        // serialize the list of custom bottom bar items to json
        string json = JsonSerializer.Serialize(bottomBarButtons);

        // save the json string to the settings
        Settings.Default.BottomButtonsJson = json;

        // save the settings
        Settings.Default.Save();
    }

    public static List<CollapsibleButton> GetBottomBarButtons(EditTextWindow editTextWindow)
    {
        List<CollapsibleButton> bottomBarButtons = new();
        Dictionary<string, RoutedCommand> _localRoutedCommands = new();
        List<MethodInfo> methods = GetMethods(editTextWindow);
        Dictionary<string, RoutedCommand> routedCommands = EditTextWindow.GetRoutedCommands();

        foreach (ButtonInfo buttonItem in GetCustomBottomBarItemsSetting())
        {
            CollapsibleButton button = new()
            {
                ButtonText = buttonItem.ButtonText,
                SymbolText = buttonItem.SymbolText,
                IsSymbol = buttonItem.IsSymbol,
                CustomButton = buttonItem
            };

            if (buttonItem.Background != "Transparent"
                && new BrushConverter()
                .ConvertFromString(buttonItem.Background) is SolidColorBrush solidColorBrush)
            {
                button.Background = solidColorBrush;
            }

            if (GetMethodInfoForName(buttonItem.ClickEvent, methods) is MethodInfo method
                && method.CreateDelegate(typeof(RoutedEventHandler), editTextWindow) is RoutedEventHandler routedEventHandler)
                button.Click += routedEventHandler;
            else
                if (GetCommandBinding(buttonItem.Command, routedCommands) is RoutedCommand routedCommand)
                button.Command = routedCommand;

            bottomBarButtons.Add(button);
        }

        return bottomBarButtons;
    }

    // a method which returns a list of all methods in this class
    private static List<MethodInfo> GetMethods(object obj)
    {
        return obj.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).ToList();
    }

    // using the above method match a method name to a string parameter
    private static MethodInfo? GetMethodInfoForName(string methodName, List<MethodInfo> methods)
    {
        foreach (MethodInfo method in methods)
            if (method.Name == methodName)
                return method;

        return null;
    }

    // a method to match a command name to a string parameter
    private static RoutedCommand? GetCommandBinding(string commandName, Dictionary<string, RoutedCommand> routedCommands)
    {
        foreach (string commandKey in routedCommands.Keys)
            if (commandKey == commandName)
                return routedCommands[commandKey];

        return null;
    }
}
