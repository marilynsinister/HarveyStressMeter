using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Вспомогательные методы для работы с рефлексией (совместимость 1.5/1.6)
    /// </summary>
    public static class ReflectionHelper
    {
        public static string AsString(object? value)
        {
            if (value == null) return "";
            
            // NetString/Netcode типы имеют свойство Value
            var prop = value.GetType().GetProperty("Value");
            if (prop != null && prop.PropertyType == typeof(string))
                return (string)(prop.GetValue(value) ?? "");
            
            return value as string ?? value.ToString() ?? "";
        }

        public static bool AsBool(object? value)
        {
            if (value == null) return false;
            
            var prop = value.GetType().GetProperty("Value");
            if (prop != null && prop.PropertyType == typeof(bool))
                return (bool)(prop.GetValue(value) ?? false);
            
            return value is bool b && b;
        }

        public static bool TryGetMember<T>(object obj, string name, out T? value)
        {
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (prop != null && typeof(T).IsAssignableFrom(prop.PropertyType))
            {
                value = (T?)prop.GetValue(obj);
                return true;
            }
            
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && typeof(T).IsAssignableFrom(field.FieldType))
            {
                value = (T?)field.GetValue(obj);
                return true;
            }
            
            value = default;
            return false;
        }

        /// <summary>
        /// Пытается получить уникальный string id квеста через известные поля.
        /// Для custom quest-ов (Content Patcher) строковый id обычно лежит в questKey или QuestId.
        /// Для vanilla квестов id может быть int или string, может дублироваться.
        /// ВАЖНО: Для квестов, добавленных через addQuest(questId), поле questKey = questId (строка).
        /// </summary>
        public static string GetQuestStringId(object quest, IMonitor? monitor = null)
        {
            // Логирование отключено в этом методе - вызывается слишком часто
            // Логирование квеста теперь делается в QuestService.AddQuest и других редких местах

            // Наиболее верный способ - поле questKey, именно оно всегда установленно при addQuest(string)
            if (TryGetMember<string>(quest, "questKey", out var s) && !string.IsNullOrWhiteSpace(s))
                return s;

            // Для кастомных — QuestId (иногда используется сторонними модами)
            if (TryGetMember<string>(quest, "QuestId", out s) && !string.IsNullOrWhiteSpace(s))
                return s;

            // Иногда id хранится как string, иногда как int (ванильные квесты)
            // 1. string id (например, "HarveyMod_SocialRecovery")
            if (TryGetMember<string>(quest, "id", out s) && !string.IsNullOrWhiteSpace(s))
                return s;

            // 2. int id (ванильные квесты — например, 3 = "Archaeology") — но это не custom!
            if (TryGetMember<int>(quest, "id", out var i) && i != 0)
                return i.ToString();

            // Иногда квест может быть Basic — и ни одно поле не сработает
            return "";
        }

        /// <summary>
        /// Универсальная функция для логирования объектов и их свойств
        /// </summary>
        /// <param name="obj">Объект для анализа</param>
        /// <param name="monitor">Монитор для логирования</param>
        /// <param name="context">Контекст для префикса логов (например, "Quest", "Buff", "NPC")</param>
        /// <param name="maxDepth">Максимальная глубина рекурсии для вложенных объектов</param>
        /// <param name="currentDepth">Текущая глубина рекурсии (внутренний параметр)</param>
        public static void LogObjectFields(object obj, IMonitor monitor, string context = "Object", int maxDepth = 2, int currentDepth = 0)
        {
            if (obj == null)
            {
                monitor.Log($"[LogObjectFields] Объект {context} равен null", LogLevel.Debug);
                return;
            }

            try
            {
                var objType = obj.GetType();
                var indent = new string(' ', currentDepth * 2);
                var prefix = $"[{context}]";
                
                if (currentDepth == 0)
                {
                    monitor.Log($"{prefix} ═══ АНАЛИЗ ОБЪЕКТА ═══", LogLevel.Debug);
                }
                
                monitor.Log($"{prefix}{indent} Тип: {objType.Name}", LogLevel.Debug);

                // Логируем все свойства
                var properties = objType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.CanRead && !IsIndexer(p))
                    .ToArray();
                
                if (properties.Length > 0)
                {
                    monitor.Log($"{prefix}{indent} Свойства ({properties.Length}):", LogLevel.Debug);
                    foreach (var prop in properties.Take(20)) // Ограничиваем количество для производительности
                    {
                        try
                        {
                            var value = prop.GetValue(obj);
                            var valueStr = FormatValue(value, monitor, context, maxDepth, currentDepth + 1);
                            monitor.Log($"{prefix}{indent}   • {prop.Name} ({prop.PropertyType.Name}) = {valueStr}", LogLevel.Debug);
                        }
                        catch (Exception ex)
                        {
                            monitor.Log($"{prefix}{indent}   • {prop.Name} = ОШИБКА: {ex.Message}", LogLevel.Debug);
                        }
                    }
                    
                    if (properties.Length > 20)
                    {
                        monitor.Log($"{prefix}{indent}   ... и еще {properties.Length - 20} свойств", LogLevel.Debug);
                    }
                }

                // Логируем все поля
                var fields = objType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => !f.IsStatic)
                    .ToArray();
                
                if (fields.Length > 0)
                {
                    monitor.Log($"{prefix}{indent} Поля ({fields.Length}):", LogLevel.Debug);
                    foreach (var field in fields.Take(20)) // Ограничиваем количество для производительности
                    {
                        try
                        {
                            var value = field.GetValue(obj);
                            var valueStr = FormatValue(value, monitor, context, maxDepth, currentDepth + 1);
                            monitor.Log($"{prefix}{indent}   • {field.Name} ({field.FieldType.Name}) = {valueStr}", LogLevel.Debug);
                        }
                        catch (Exception ex)
                        {
                            monitor.Log($"{prefix}{indent}   • {field.Name} = ОШИБКА: {ex.Message}", LogLevel.Debug);
                        }
                    }
                    
                    if (fields.Length > 20)
                    {
                        monitor.Log($"{prefix}{indent}   ... и еще {fields.Length - 20} полей", LogLevel.Debug);
                    }
                }

                if (currentDepth == 0)
                {
                    monitor.Log($"{prefix} ═══ КОНЕЦ АНАЛИЗА ═══", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"[LogObjectFields] ❌ Ошибка при анализе объекта {context}: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Форматирует значение для логирования с учетом типов и глубины рекурсии
        /// </summary>
        private static string FormatValue(object? value, IMonitor monitor, string context, int maxDepth, int currentDepth)
        {
            if (value == null) return "null";
            
            var valueType = value.GetType();
            
            // Простые типы
            if (valueType.IsPrimitive || valueType == typeof(string) || valueType == typeof(decimal))
            {
                var str = value.ToString();
                return str?.Length > 100 ? $"'{str.Substring(0, 100)}...'" : $"'{str}'";
            }
            
            // Коллекции
            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                try
                {
                    var items = enumerable.Cast<object>().Take(5).ToArray();
                    if (items.Length == 0) return "[]";
                    
                    var itemStrs = items.Select(item => 
                        item?.ToString()?.Length > 50 ? 
                        $"{item.ToString()?.Substring(0, 50)}..." : 
                        item?.ToString() ?? "null"
                    ).ToArray();
                    
                    var result = $"[{string.Join(", ", itemStrs)}]";
                    if (enumerable.Cast<object>().Count() > 5)
                        result += $" ... и еще {enumerable.Cast<object>().Count() - 5} элементов";
                    
                    return result;
                }
                catch
                {
                    return $"[коллекция {valueType.Name}]";
                }
            }
            
            // Сложные объекты - рекурсивный анализ
            if (currentDepth < maxDepth && !valueType.IsPrimitive && valueType != typeof(string))
            {
                try
                {
                    // Проверяем, есть ли у объекта важные свойства для отображения
                    var keyProperties = valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Where(p => p.CanRead && (p.Name.ToLower().Contains("id") || p.Name.ToLower().Contains("name") || p.Name.ToLower().Contains("key")))
                        .Take(3)
                        .ToArray();
                    
                    if (keyProperties.Length > 0)
                    {
                        var keyValues = keyProperties.Select(p => 
                        {
                            try
                            {
                                var val = p.GetValue(value);
                                return $"{p.Name}={val}";
                            }
                            catch
                            {
                                return $"{p.Name}=?";
                            }
                        }).ToArray();
                        
                        return $"{{{string.Join(", ", keyValues)}}}";
                    }
                    
                    return $"{{{valueType.Name}}}";
                }
                catch
                {
                    return $"{{{valueType.Name}}}";
                }
            }
            
            // Обычное строковое представление
            var strValue = value.ToString();
            return strValue?.Length > 100 ? $"'{strValue.Substring(0, 100)}...'" : $"'{strValue}'";
        }

        /// <summary>
        /// Проверяет, является ли свойство индексатором
        /// </summary>
        private static bool IsIndexer(PropertyInfo property)
        {
            return property.GetIndexParameters().Length > 0;
        }

        // ===== УДОБНЫЕ МЕТОДЫ-ОБЕРТКИ =====

        /// <summary>
        /// Логирует поля квеста (специализированная версия для квестов)
        /// </summary>
        public static void LogQuest(object quest, IMonitor monitor)
        {
            LogObjectFields(quest, monitor, "Quest");
        }

        /// <summary>
        /// Логирует поля баффа (специализированная версия для баффов)
        /// </summary>
        public static void LogBuff(object buff, IMonitor monitor)
        {
            LogObjectFields(buff, monitor, "Buff");
        }

        /// <summary>
        /// Логирует поля NPC (специализированная версия для NPC)
        /// </summary>
        public static void LogNPC(object npc, IMonitor monitor)
        {
            LogObjectFields(npc, monitor, "NPC");
        }

        /// <summary>
        /// Логирует поля игрока (специализированная версия для игрока)
        /// </summary>
        public static void LogPlayer(object player, IMonitor monitor)
        {
            LogObjectFields(player, monitor, "Player");
        }

        /// <summary>
        /// Логирует поля локации (специализированная версия для локаций)
        /// </summary>
        public static void LogLocation(object location, IMonitor monitor)
        {
            LogObjectFields(location, monitor, "Location");
        }

        /// <summary>
        /// Логирует поля предмета (специализированная версия для предметов)
        /// </summary>
        public static void LogItem(object item, IMonitor monitor)
        {
            LogObjectFields(item, monitor, "Item");
        }
    }
}

