using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GlassBoard;

internal sealed record SavedItem(string Target, string? Category, bool Pinned);
internal sealed record BoardState(List<string> Categories, List<SavedItem> Items, int? DockCount = null,
    int Transparency = 56, double? LockedCenter = null, double? LockedTop = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PositionLocked => LockedCenter.HasValue && LockedTop.HasValue;
}

internal sealed class BoardLibrary
{
    private readonly string file;
    private readonly Dictionary<string, DockItem> icons = new(StringComparer.OrdinalIgnoreCase);
    public BoardState State { get; private set; } = new([], [
        new("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", null, true),
        new("::{645FF040-5081-101B-9F08-00AA002F954E}", null, true)], 6);
    public event Action? Changed;

    public BoardLibrary(string file) => this.file = file;

    public void Load()
    {
        if (!File.Exists(file)) return;
        var state = JsonSerializer.Deserialize<BoardState>(File.ReadAllText(file));
        if (state?.Items is null || state.Categories is null ||
            state.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Target)) ||
            state.Categories.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("配置文件格式无效。");
        State = state.DockCount == null
            ? state with { Items = state.Items.OrderByDescending(i => i.Pinned).ToList(),
                           DockCount = Math.Max(6, state.Items.Count(i => i.Pinned)) }
            : state with { DockCount = Math.Max(0, state.DockCount.Value) };
        State = State with { Transparency = Math.Clamp(State.Transparency, 0, 100) };
    }

    public DockItem? Icon(string target)
    {
        if (icons.TryGetValue(target, out var cached)) return cached;
        var item = ShellDrop.Resolve(target);
        if (item != null) icons[target] = item;
        return item;
    }

    public void ReleasePanelIcons()
    {
        var retained = State.Items.Where(i => icons.ContainsKey(i.Target)).Take(State.DockCount!.Value)
            .Select(i => i.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in icons.Keys.Where(k => !retained.Contains(k)).ToArray()) icons.Remove(key);
    }

    public void AddCategory(string name)
    {
        if (State.Categories.Contains(name, StringComparer.CurrentCultureIgnoreCase) ||
            name == "全部") return;
        Save(State with { Categories = [..State.Categories, name] });
    }

    public void RemoveCategory(string name)
    {
        if (!State.Categories.Contains(name)) return;
        Save(State with {
            Categories = State.Categories.Where(c => c != name).ToList(),
            Items = State.Items.Select(i => i.Category == name ? i with { Category = null } : i).ToList()
        });
    }

    public void Import(IEnumerable<DockItem> additions, bool pin, string? category)
    {
        var entries = new List<SavedItem>(State.Items);
        foreach (var item in additions)
        {
            icons[item.Target] = item;
            int at = entries.FindIndex(i => StringComparer.OrdinalIgnoreCase.Equals(i.Target, item.Target));
            var entry = at < 0 ? new SavedItem(item.Target, category, false)
                : entries[at] with { Category = category ?? entries[at].Category };
            int limit = State.DockCount!.Value;
            if (pin && limit > 0 && (at < 0 || at >= limit))
            {
                if (at >= 0) entries.RemoveAt(at);
                entries.Insert(Math.Min(entries.Count, limit - 1), entry);
            }
            else if (at < 0) entries.Add(entry);
            else entries[at] = entry;
        }
        if (!entries.SequenceEqual(State.Items)) Save(State with { Items = entries });
    }

    public void SetDockCount(int count)
    {
        if (count >= 0 && State.DockCount != count) Save(State with { DockCount = count });
    }

    public void SetDockPosition(double? center, double? top) =>
        Save(State with { LockedCenter = center, LockedTop = top });

    public void SetTransparency(int percent)
    {
        if (percent != State.Transparency) Save(State with { Transparency = Math.Clamp(percent, 0, 100) });
    }

    public void Remove(string target)
    {
        var entries = State.Items.Where(i => !StringComparer.OrdinalIgnoreCase.Equals(i.Target, target)).ToList();
        if (entries.Count == State.Items.Count) return;
        Save(State with { Items = entries });
        icons.Remove(target);
    }

    public void Move(string target, string anchor, bool after)
    {
        var entries = new List<SavedItem>(State.Items);
        var source = entries.Find(i => StringComparer.OrdinalIgnoreCase.Equals(i.Target, target));
        var destination = entries.Find(i => StringComparer.OrdinalIgnoreCase.Equals(i.Target, anchor));
        if (source == null || destination == null || source == destination) return;
        entries.Remove(source);
        entries.Insert(entries.IndexOf(destination) + (after ? 1 : 0), source);
        if (!entries.SequenceEqual(State.Items)) Save(State with { Items = entries });
    }

    public void Categorize(string target, string? category)
    {
        var entries = State.Items.Select(i => StringComparer.OrdinalIgnoreCase.Equals(target, i.Target)
            ? i with { Category = category } : i).ToList();
        if (!entries.SequenceEqual(State.Items)) Save(State with { Items = entries });
    }

    private void Save(BoardState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, true);
        State = state;
        Changed?.Invoke();
    }
}
