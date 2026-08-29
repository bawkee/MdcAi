#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.ChatUI.Views;

using Microsoft.UI.Xaml.Controls;
using OpenAiApi;

/// <summary>
/// Builds provider-grouped model picker menus, the WinUI sibling of dsh's grouped model
/// chooser. Models are grouped in two levels so it's always obvious where they come from:
///   OpenAI
///     └─ (models)
///   OpenRouter
///     ├─ anthropic
///     │  └─ (models)
///     ├─ openai          ← OpenRouter's OpenAI-hosted models live here, clearly separate
///     │  └─ (models)
///     └─ ...
/// A 400+ model catalog never shows up as a giant flat list, and OpenAI vs OpenRouter models
/// are never confused.
/// </summary>
public static class ModelMenuFactory
{
    /// <summary>
    /// Produces menu items for the given models, grouped first by provider, then by the
    /// provider's group key (author for OpenRouter, "OpenAI" for OpenAI).
    /// </summary>
    public static IReadOnlyList<MenuFlyoutItemBase> BuildProviderGroupedMenu(
        IEnumerable<AiModel> models,
        Action<AiModel> onSelect)
    {
        var ret = new List<MenuFlyoutItemBase>();

        foreach (var providerGroup in models
                     .GroupBy(m => m.ProviderKey)
                     .OrderBy(g => g.Key == AiProviders.OpenAiKey ? 0 : 1)   // OpenAI first
                     .ThenBy(g => AiProviders.Get(g.Key).DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var provider = AiProviders.Get(providerGroup.Key);
            var providerSub = new MenuFlyoutSubItem { Text = provider.DisplayName };

            var innerGroups = providerGroup
                              .GroupBy(m => m.GroupKey ?? "Other")
                              .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                              .ToList();

            foreach (var inner in innerGroups)
            {
                var items = inner.OrderBy(m => m.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                                 .Select(m => BuildItem(m, onSelect))
                                 .ToList();

                // One inner group (e.g. OpenAI) -> models go straight under the provider item;
                // multiple groups (e.g. OpenRouter authors) -> a submenu per group.
                if (innerGroups.Count == 1)
                    foreach (var item in items)
                        providerSub.Items.Add(item);
                else
                {
                    var innerSub = new MenuFlyoutSubItem { Text = inner.Key };
                    foreach (var item in items)
                        innerSub.Items.Add(item);
                    providerSub.Items.Add(innerSub);
                }
            }

            if (providerSub.Items.Count > 0)
                ret.Add(providerSub);
        }

        return ret;
    }

    /// <summary>Label for picker buttons: "ProviderName · ModelName" so the origin stays visible.</summary>
    public static string LabelFor(AiModel model)
    {
        if (model == null)
            return "Select model";
        return $"{AiProviders.Get(model.ProviderKey).DisplayName} · {model.DisplayLabel}";
    }

    /// <summary>
    /// The "Effort: X" submenu with one item per supported level. Always present in the flyout
    /// (never hidden - menus jumping around are rude), and disabled when the current model has
    /// no effort support. The header shows the current level ("Effort: Medium"), just "Effort"
    /// when nothing is selected yet.
    /// </summary>
    public static MenuFlyoutSubItem BuildEffortSubMenu(string currentEffort, IReadOnlyList<string> supportedEfforts, Action<string> onSelect)
    {
        var sub = new MenuFlyoutSubItem
        {
            Text = currentEffort == null ? "Effort" : $"Effort: {currentEffort}",
            IsEnabled = supportedEfforts != null && supportedEfforts.Count > 0
        };

        if (supportedEfforts != null)
        {
            foreach (var level in supportedEfforts)
            {
                var item = new MenuFlyoutItem
                {
                    Text = level,
                    CommandParameter = level
                };

                item.Click += (_, _) => onSelect(level);

                sub.Items.Add(item);
            }
        }

        return sub;
    }

    private static MenuFlyoutItem BuildItem(AiModel model, Action<AiModel> onSelect)
    {
        var item = new MenuFlyoutItem
        {
            Text = model.DisplayLabel,
            CommandParameter = model.ModelID
        };

        item.Click += (_, _) => onSelect(model);

        return item;
    }
}