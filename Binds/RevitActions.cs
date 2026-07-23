using System.Collections.Generic;

namespace RevitQuickAccess.Binds
{
    /// <summary>One bindable Revit action: a Russian label, the token stored as the bind command
    /// (a PostableCommand enum name or an ID_ command id), and a group for the picker.</summary>
    public sealed class RevitAction
    {
        public string Group { get; }
        public string Label { get; }
        public string Token { get; }

        public RevitAction(string group, string label, string token)
        {
            Group = group; Label = label; Token = token;
        }

        public override string ToString() => Label;
    }

    /// <summary>
    /// Curated list of actions that can't be recorded by clicking the ribbon — right-click / context-menu
    /// items (hide element, hide category, override graphics…) and the view control bar (reveal hidden
    /// elements). Every token here is a Revit PostableCommand name, which CommandExecutor already resolves
    /// and posts; posted commands act on the current selection, so a bind fires them on the selected object.
    /// Names were verified against RevitAPIUI.dll for Revit 2026.
    /// </summary>
    public static class RevitActions
    {
        public static readonly List<RevitAction> All = new List<RevitAction>
        {
            // --- hide / isolate / reveal (right-click «Скрыть в виде», bottom light-bulb) ---
            new RevitAction("Скрытие и показ", "Скрыть элемент",                       "HideElements"),
            new RevitAction("Скрытие и показ", "Скрыть категорию",                     "HideCategory"),
            new RevitAction("Скрытие и показ", "Показать скрытые элементы (лампочка)", "ToggleRevealHiddenElementsMode"),

            // --- graphic overrides (right-click «Переопределить в виде») ---
            new RevitAction("Переопределение", "Переопределить по элементу",  "OverrideByElement"),
            new RevitAction("Переопределение", "Переопределить по категории",  "OverrideByCategory"),
            new RevitAction("Переопределение", "Переопределить по фильтру",    "OverrideByFilter"),
            new RevitAction("Переопределение", "Видимость/графика (VG)",       "VisibilityOrGraphics"),
            new RevitAction("Переопределение", "Фильтры вида",                 "Filters"),

            // --- editing (context menu on a selected element) ---
            new RevitAction("Правка",   "Удалить",                       "Delete"),
            new RevitAction("Правка",   "Копировать",                    "Copy"),
            new RevitAction("Правка",   "Переместить",                   "Move"),
            new RevitAction("Правка",   "Повернуть",                     "Rotate"),
            new RevitAction("Правка",   "Зеркало — выбрать ось",         "MirrorPickAxis"),
            new RevitAction("Правка",   "Зеркало — нарисовать ось",      "MirrorDrawAxis"),
            new RevitAction("Правка",   "Массив",                        "Array"),
            new RevitAction("Правка",   "Масштаб",                       "Scale"),
            new RevitAction("Правка",   "Выровнять",                     "Align"),
            new RevitAction("Правка",   "Сместить (Offset)",             "Offset"),
            new RevitAction("Правка",   "Разрезать элемент",             "SplitElement"),
            new RevitAction("Правка",   "Обрезать/удлинить до угла",     "TrimOrExtendToCorner"),
            new RevitAction("Правка",   "Обрезать/удлинить один",        "TrimOrExtendSingleElement"),
            new RevitAction("Правка",   "Обрезать/удлинить несколько",   "TrimOrExtendMultipleElements"),
            new RevitAction("Правка",   "Закрепить",                     "Pin"),
            new RevitAction("Правка",   "Открепить",                     "Unpin"),
            new RevitAction("Правка",   "Снести (Demolish)",             "Demolish"),

            // --- selection / clipboard / groups ---
            new RevitAction("Выбор и буфер", "Копировать в буфер",        "CopyToClipboard"),
            new RevitAction("Выбор и буфер", "Вставить из буфера",        "PasteFromClipboard"),
            new RevitAction("Выбор и буфер", "Рамка сечения (Selection Box)", "SelectionBox"),
            new RevitAction("Выбор и буфер", "Создать группу",            "CreateGroup"),
            new RevitAction("Выбор и буфер", "Сохранить выбор",           "SaveSelection"),
            new RevitAction("Выбор и буфер", "Загрузить выбор",           "LoadSelection"),
            new RevitAction("Выбор и буфер", "Редактировать выбор",       "EditSelection"),
        };
    }
}
