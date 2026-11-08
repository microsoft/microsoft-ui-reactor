namespace ReactorRegedit;

/// <summary>
/// Localizable string constants for the Registry Editor UI.
/// Structured as constants for now; in a full localization pass these would
/// be backed by ResourceManager lookups from Strings.resx.
/// </summary>
internal static class Strings
{
    // Value list columns
    public const string ColumnName = "Name";
    public const string ColumnType = "Type";
    public const string ColumnData = "Data";

    // Context menu
    public const string Modify = "Modify...";
    public const string ModifyBinaryData = "Modify Binary Data...";
    public const string Delete = "Delete";
    public const string Rename = "Rename";

    // Menu bar - File
    public const string FileMenu = "File";
    public const string Import = "Import...";
    public const string Export = "Export...";
    public const string LoadHive = "Load Hive...";
    public const string UnloadHive = "Unload Hive...";
    public const string ConnectNetworkRegistry = "Connect Network Registry...";
    public const string DisconnectNetworkRegistry = "Disconnect Network Registry...";
    public const string Print = "Print...";
    public const string Exit = "Exit";

    // Menu bar - Edit
    public const string EditMenu = "Edit";
    public const string NewMenu = "New";
    public const string NewKey = "Key";
    public const string NewStringValue = "String Value";
    public const string NewBinaryValue = "Binary Value";
    public const string NewDwordValue = "DWORD (32-bit) Value";
    public const string NewQwordValue = "QWORD (64-bit) Value";
    public const string NewMultiStringValue = "Multi-String Value";
    public const string NewExpandableStringValue = "Expandable String Value";
    public const string Permissions = "Permissions...";
    public const string CopyKeyName = "Copy Key Name";
    public const string Find = "Find...";
    public const string FindNext = "Find Next";

    // Menu bar - View
    public const string ViewMenu = "View";
    public const string AddressBar = "Address Bar";
    public const string StatusBar = "Status Bar";
    public const string Split = "Split";
    public const string DisplayBinaryData = "Display Binary Data";
    public const string Refresh = "Refresh";
    public const string Font = "Font...";

    // Menu bar - Favorites
    public const string FavoritesMenu = "Favorites";
    public const string AddToFavorites = "Add to Favorites...";
    public const string RemoveFromFavorites = "Remove Favorite...";

    // Menu bar - Help
    public const string HelpMenu = "Help";
    public const string About = "About Registry Editor";

    // Dialogs
    public const string EditStringTitle = "Edit String";
    public const string EditMultiStringTitle = "Edit Multi-String";
    public const string EditDwordTitle = "Edit DWORD (32-bit) Value";
    public const string EditQwordTitle = "Edit QWORD (64-bit) Value";
    public const string EditBinaryTitle = "Edit Binary Value";
    public const string ValueName = "Value name:";
    public const string ValueData = "Value data:";
    public const string Base = "Base";
    public const string Hexadecimal = "Hexadecimal";
    public const string Decimal = "Decimal";
    public const string OK = "OK";
    public const string Cancel = "Cancel";

    // Find dialog
    public const string FindTitle = "Find";
    public const string FindWhat = "Find what:";
    public const string LookAt = "Look at";
    public const string FindKeys = "Keys";
    public const string FindValues = "Values";
    public const string FindData = "Data";
    public const string MatchWholeStringOnly = "Match whole string only";

    // Favorites dialog
    public const string AddFavoriteTitle = "Add to Favorites";
    public const string FavoriteName = "Favorite name:";
    public const string RemoveFavoriteTitle = "Remove Favorites";
    public const string SelectFavorite = "Select Favorite:";

    // Export dialog
    public const string ExportTitle = "Export Registry File";
    public const string RegFilesFilter = "Registration Files";
    public const string ImportTitle = "Import Registry File";
    public const string ExportRange = "Export range";
    public const string All = "All";
    public const string SelectedBranch = "Selected branch";

    // Status bar
    public const string Computer = "Computer";
    public const string Ready = "Ready";

    // Confirmations
    public const string ConfirmDeleteKey = "Are you sure you want to permanently delete this key and all of its subkeys?";
    public const string ConfirmDeleteValue = "Deleting certain registry values could cause system instability. Are you sure you want to permanently delete this value?";
    public const string ConfirmTitle = "Confirm";

    // About
    public const string AboutTitle = "About Registry Editor";
    public const string AboutText = "Registry Editor\nReactor WinUI 3 Edition";

    // Errors
    public const string ErrorTitle = "Error";
    public const string ErrorCannotDeleteRoot = "Cannot delete a root key.";
    public const string ErrorAccessDenied = "Access is denied.";
    public const string ErrorKeyNotFound = "The specified key was not found.";
    public const string RenameKeyTitle = "Rename Key";
    public const string RenameValueTitle = "Rename Value";
    public const string NewKeyName = "New name:";

    public const string EditExpandStringTitle = "Edit Expandable String";
    public const string InvalidHex = "Enter a hexadecimal number in range.";
    public const string InvalidDecimal = "Enter a decimal number in range.";
    public const string ConfirmKeyDeleteTitle = "Confirm Key Delete";
    public const string ConfirmValueDeleteTitle = "Confirm Value Delete";
    public const string ConfirmDeleteValues = "Deleting certain registry values could cause system instability. Are you sure you want to permanently delete these values?";
    public const string MultiStringEmptyRemoved = "Data of type REG_MULTI_SZ cannot contain empty strings. Registry Editor removed all empty strings found.";
    public const string Expand = "Expand";
    public const string Collapse = "Collapse";
    public const string AddressBarName = "Address";
    public const string GoToAddressBar = "Go to Address Bar";
}
