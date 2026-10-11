using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using UnityEngine;

namespace BeaverBuddies.DesyncDetecter
{
    /**
     * An object's own state in a line, for the traces: its numbers, flags and
     * names, and what it points at by entity. A trace that says an executor
     * ticked, with only its elapsed time, can't say what made two machines'
     * executors differ; with the rest of what it holds, the line itself does.
     */
    public static class StateReflection
    {
        private static readonly Dictionary<Type, FieldInfo[]> fieldsOf = new();
        private const int MaxChars = 700;

        private static string Bonuses(Timberborn.BonusSystem.BonusManager manager)
        {
            var sb = new StringBuilder();
            try
            {
                var dictionary = (System.Collections.IDictionary)typeof(Timberborn.BonusSystem.BonusManager)
                    .GetField("_bonuses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                var keys = new List<string>();
                foreach (object key in dictionary.Keys) keys.Add((string)key);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    object bonus = dictionary[key];
                    var value = bonus.GetType().GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bonus);
                    sb.Append(key).Append('=').Append(((float)value).ToString("R")).Append(' ');
                }
            }
            catch (Exception e)
            {
                sb.Append($"(bonuses: {e.GetType().Name}) ");
            }
            return sb.ToString();
        }

        private static FieldInfo[] FieldsOf(Type type)
        {
            if (fieldsOf.TryGetValue(type, out FieldInfo[] fields)) return fields;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(object) && t != typeof(BaseComponent); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (IsPlain(f.FieldType) || typeof(BaseComponent).IsAssignableFrom(f.FieldType)) list.Add(f);
                }
            }
            return fieldsOf[type] = list.ToArray();
        }

        private static bool IsPlain(Type t) =>
            t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector3) || t == typeof(Vector3Int) || t == typeof(Vector2) || t == typeof(Guid);

        public static string Describe(object o)
        {
            if (o == null) return "";
            var sb = new StringBuilder();
            try
            {
                // The entity's own working speed and bonuses first: they're sums that two machines can add up in
                // another order, and a difference in the last digit moves when a job ends by a whole tick
                if (o is BaseComponent owner && owner)
                {
                    var worker = owner.GetComponent<Timberborn.WorkSystem.Worker>();
                    if (worker) sb.Append("speed=").Append(worker.WorkingSpeedMultiplier.ToString("R")).Append(' ');
                    var bonuses = owner.GetComponent<Timberborn.BonusSystem.BonusManager>();
                    if (bonuses) sb.Append(Bonuses(bonuses));
                }
                string self = o is BaseComponent b && b ? b.GetComponent<EntityComponent>()?.EntityId.ToString().Substring(0, 8) : null;
                // Plain values before references, which are many and say less
                foreach (bool references in new[] { false, true })
                foreach (FieldInfo f in FieldsOf(o.GetType()))
                {
                    if (typeof(BaseComponent).IsAssignableFrom(f.FieldType) != references) continue;
                    object v = f.GetValue(o);
                    string text;
                    if (v is BaseComponent component)
                    {
                        // A destroyed one reads as null
                        if (!component) continue;
                        var entity = component.GetComponent<EntityComponent>();
                        string id = entity ? entity.EntityId.ToString().Substring(0, 8) : null;
                        // Its own components say nothing: what it points at elsewhere does
                        if (id != null && id == self) continue;
                        text = id != null ? $"{id}:{component.GetType().Name}" : component.GetType().Name;
                    }
                    else if (v is float x) text = x.ToString("R");
                    else if (v is double d) text = d.ToString("R");
                    else if (v == null) continue;
                    else text = v.ToString();
                    sb.Append(f.Name).Append('=').Append(text).Append(' ');
                    if (sb.Length > MaxChars) break;
                }
            }
            catch (Exception e)
            {
                sb.Append($"(couldn't read: {e.GetType().Name})");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
