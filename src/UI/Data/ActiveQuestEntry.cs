/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LoneEftDmaRadar.UI.Data
{
    /// <summary>
    /// UI entry for an active quest with blacklist toggle.
    /// </summary>
    public sealed class ActiveQuestEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isEnabled;

        public ActiveQuestEntry(string id, string questName, string traderName, bool kappaRequired, bool lightkeeperRequired, string mapName = null)
        {
            Id = id;
            QuestName = questName;
            TraderName = traderName;
            KappaRequired = kappaRequired;
            LightkeeperRequired = lightkeeperRequired;
            MapName = mapName ?? "Any";

            // Check if quest is currently blacklisted (inverted logic)
            _isEnabled = !App.Config.QuestHelper.BlacklistedQuests.ContainsKey(id);
        }

        public string Id { get; }
        public string QuestName { get; }
        public string TraderName { get; }
        public bool KappaRequired { get; }
        public bool LightkeeperRequired { get; }
        public string MapName { get; }

        /// <summary>
        /// Sort key that pushes "Any" to the bottom of the list.
        /// </summary>
        public string MapSortKey => MapName == "Any" ? "zzz" : MapName;

        /// <summary>
        /// Quest objectives for display.
        /// </summary>
        public ObservableCollection<QuestObjectiveEntry> Objectives { get; } = new();

        public string DisplayName => $"[{TraderName}] {QuestName}";
        public string Badges
        {
            get
            {
                if (KappaRequired) return "[Kappa]";
                if (LightkeeperRequired) return "[Lightkeeper]";
                return string.Empty;
            }
        }

        /// <summary>
        /// True if quest is enabled (NOT blacklisted), false if disabled (blacklisted).
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;

                    // Update blacklist (inverted logic)
                    if (_isEnabled)
                    {
                        // Enable = remove from blacklist
                        App.Config.QuestHelper.BlacklistedQuests.TryRemove(Id, out _);
                    }
                    else
                    {
                        // Disable = add to blacklist
                        App.Config.QuestHelper.BlacklistedQuests.TryAdd(Id, 0);
                    }

                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// UI entry for a single quest objective.
    /// </summary>
    public sealed class QuestObjectiveEntry
    {
        public QuestObjectiveEntry(string id, string description, string typeLabel, bool isCompleted, int current, int target)
        {
            Id = id;
            Description = description;
            TypeLabel = typeLabel;
            IsCompleted = isCompleted;
            Current = current;
            Target = target;
        }

        public string Id { get; }
        public string Description { get; }
        public string TypeLabel { get; }
        public bool IsCompleted { get; }
        public int Current { get; }
        public int Target { get; }

        /// <summary>
        /// Progress text: "3/5" for counted objectives, checkmark/X for binary ones.
        /// </summary>
        public string ProgressText => Target > 1 ? $"{Current}/{Target}" : (IsCompleted ? "\u2713" : "\u2717");

        /// <summary>
        /// 0.0 to 1.0 for progress bar width.
        /// </summary>
        public double ProgressFraction => Target > 0 ? Math.Min(1.0, (double)Current / Target) : 0;

        public string StatusIcon => IsCompleted ? "+" : "-";
    }

    /// <summary>
    /// An item that needs to be brought into raid for a quest.
    /// </summary>
    public sealed class BringItemEntry
    {
        public BringItemEntry(string itemName, string category, string questName)
        {
            ItemName = itemName;
            Category = category;
            QuestName = questName;
        }

        public string ItemName { get; }
        public string Category { get; }
        public string QuestName { get; }
    }
}
