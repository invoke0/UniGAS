using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 存放属性数据的结构体，包含 BaseValue（永久值）和 CurrentValue（当前值/临时值）。
/// 参考 FGameplayAttributeData
/// </summary>
[Serializable]
public struct GameplayAttributeData
{
    [SerializeField]
    private float BaseValue;

    [SerializeField]
    private float CurrentValue;

    public GameplayAttributeData(float DefaultValue)
    {
        BaseValue = DefaultValue;
        CurrentValue = DefaultValue;
    }

    public float GetCurrentValue()
    {
        return CurrentValue;
    }

    public void SetCurrentValue(float NewValue)
    {
        CurrentValue = NewValue;
    }

    public float GetBaseValue()
    {
        return BaseValue;
    }

    public void SetBaseValue(float NewValue)
    {
        BaseValue = NewValue;
    }
}

/// <summary>
/// 属性的标识符，用于在 AttributeSet 中查找和访问具体的属性。
/// 内部使用反射缓存来优化性能。
/// 参考 FGameplayAttribute
/// </summary>
[Serializable]
public struct GameplayAttribute : IEquatable<GameplayAttribute>
{
    public string AttributeName;
    
    [SerializeField]
    private string _attributeOwnerName; // UE中对应 AttributeSet 类型名称的缓存
    
    // 对应 UE 中的 TFieldPath<FProperty> Attribute
    // 在 C# 中我们用 FieldInfo 或 PropertyInfo 来模拟 FProperty*
    [NonSerialized]
    private FieldInfo Attribute; 
    
    [NonSerialized]
    private PropertyInfo AttributeProperty; // 兼容 C# 属性

    // 全局反射缓存
    private static Dictionary<(Type, string), FieldInfo> _fieldCache = new Dictionary<(Type, string), FieldInfo>();
    private static Dictionary<(Type, string), PropertyInfo> _propertyCache = new Dictionary<(Type, string), PropertyInfo>();

    public Type AttributeSetType 
    {
        get 
        {
            if (Attribute != null) return Attribute.DeclaringType;
            if (AttributeProperty != null) return AttributeProperty.DeclaringType;
            return null;
        }
    }

    // 辅助检查方法
    public static bool IsSupportedProperty(FieldInfo field)
    {
        return field != null && field.FieldType == typeof(GameplayAttributeData);
    }

    public static bool IsSupportedProperty(PropertyInfo property)
    {
        return property != null && property.PropertyType == typeof(GameplayAttributeData);
    }

    public GameplayAttribute(FieldInfo field)
    {
        if (IsSupportedProperty(field))
        {
            AttributeName = field.Name;
            _attributeOwnerName = field.DeclaringType.Name;
            Attribute = field;
            AttributeProperty = null;
        }
        else
        {
            Debug.LogError($"Invalid field '{field?.Name}' for GameplayAttribute. Type must be GameplayAttributeData.");
            AttributeName = string.Empty;
            _attributeOwnerName = string.Empty;
            Attribute = null;
            AttributeProperty = null;
        }
    }

    public GameplayAttribute(PropertyInfo property)
    {
        if (IsSupportedProperty(property))
        {
            AttributeName = property.Name;
            _attributeOwnerName = property.DeclaringType.Name;
            AttributeProperty = property;
            Attribute = null;
        }
        else
        {
            Debug.LogError($"Invalid property '{property?.Name}' for GameplayAttribute. Type must be GameplayAttributeData.");
            AttributeName = string.Empty;
            _attributeOwnerName = string.Empty;
            Attribute = null;
            AttributeProperty = null;
        }
    }

    public GameplayAttribute(Type setType, string name)
    {
        AttributeName = name;
        _attributeOwnerName = setType.Name;
        Attribute = null;
        AttributeProperty = null;
        
        // 尝试解析
        var key = (setType, name);
        if (!_fieldCache.TryGetValue(key, out Attribute))
        {
            var field = setType.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (IsSupportedProperty(field))
            {
                Attribute = field;
                _fieldCache[key] = Attribute;
            }
        }

        if (Attribute == null)
        {
            if (!_propertyCache.TryGetValue(key, out AttributeProperty))
            {
                var prop = setType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (IsSupportedProperty(prop))
                {
                    AttributeProperty = prop;
                    _propertyCache[key] = AttributeProperty;
                }
            }
        }
        
        if (!IsValid() && !string.IsNullOrEmpty(name))
        {
            Debug.LogWarning($"Could not find GameplayAttributeData field/property '{name}' on set '{setType.Name}'.");
        }
    }

    public bool IsValid()
    {
        return (Attribute != null || AttributeProperty != null);
    }

    public string GetName() => AttributeName;

    /// <summary>
    /// 获取关联的 AttributeSet 类型
    /// </summary>
    public Type GetAttributeSetType() => AttributeSetType;

    /// <summary>
    /// 获取属性对应的 FieldInfo（如果是字段）
    /// </summary>
    public FieldInfo GetUProperty()
    {
        return Attribute;
    }
    
    /// <summary>
    /// 获取属性对应的 PropertyInfo（如果是属性）
    /// </summary>
    public PropertyInfo GetPropertyInfo()
    {
        return AttributeProperty;
    }

    /// <summary>
    /// 从 AttributeSet 实例中获取 GameplayAttributeData 的值
    /// </summary>
    public GameplayAttributeData? GetGameplayAttributeData(AttributeSet Src)
    {
        if (Src == null) return null;

        if (Attribute != null)
        {
            return (GameplayAttributeData)Attribute.GetValue(Src);
        }
        
        if (AttributeProperty != null)
        {
            return (GameplayAttributeData)AttributeProperty.GetValue(Src);
        }

        return null;
    }

    /// <summary>
    /// 设置 AttributeSet 中该属性的 CurrentValue
    /// </summary>
    public void SetNumericValueChecked(float NewValue, AttributeSet Dest)
    {
        if (Dest == null) return;

        if (Attribute != null)
        {
            // 结构体是值类型，必须取出来修改后再设置回去
            var data = (GameplayAttributeData)Attribute.GetValue(Dest);
            float OldValue = data.GetCurrentValue();

            // 触发 PreAttributeChange
            Dest.PreAttributeChange(this, ref NewValue);
            
            data.SetCurrentValue(NewValue);
            Attribute.SetValue(Dest, data);

            // 触发 PostAttributeChange
            Dest.PostAttributeChange(this, OldValue, NewValue);
            return;
        }

        if (AttributeProperty != null)
        {
            var data = (GameplayAttributeData)AttributeProperty.GetValue(Dest);
            float OldValue = data.GetCurrentValue();

            Dest.PreAttributeChange(this, ref NewValue);

            data.SetCurrentValue(NewValue);
            AttributeProperty.SetValue(Dest, data);

            Dest.PostAttributeChange(this, OldValue, NewValue);
        }
    }

    /// <summary>
    /// 直接获取属性的 CurrentValue
    /// </summary>
    public float GetNumericValue(AttributeSet Src)
    {
        var data = GetGameplayAttributeData(Src);
        return data.HasValue ? data.Value.GetCurrentValue() : 0f;
    }

    #region Equality Implementation
    public bool Equals(GameplayAttribute other)
    {
        // 只要名字和OwnerName相同，即使反射对象不同也视为相同（序列化场景）
        // 如果反射对象存在，优先比较反射对象
        if (Attribute != null && other.Attribute != null) return Attribute == other.Attribute;
        if (AttributeProperty != null && other.AttributeProperty != null) return AttributeProperty == other.AttributeProperty;

        return AttributeName == other.AttributeName && _attributeOwnerName == other._attributeOwnerName;
    }

    public override bool Equals(object obj)
    {
        return obj is GameplayAttribute other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AttributeName, _attributeOwnerName);
    }

    public static bool operator ==(GameplayAttribute left, GameplayAttribute right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GameplayAttribute left, GameplayAttribute right)
    {
        return !left.Equals(right);
    }
    #endregion
}

/// <summary>
/// 包含修改属性时的上下文数据。
/// 目前作为占位符，后续实现 GameplayEffect 时需要完善。
/// 参考 FGameplayEffectModCallbackData
/// </summary>
public struct GameplayEffectModCallbackData
{
    public GameplayEffectSpec EffectSpec; // 需要后续定义 GameplayEffectSpec
    public ModifierSpec EvaluatedData;    // 需要后续定义
    public AbilitySystemComponent Target;
}

// 临时占位符，避免编译错误，后续实现 GE 时替换
public class GameplayEffectSpec { }
public struct ModifierSpec { public float Magnitude; }


/// <summary>
/// 定义游戏属性集的基类。
/// 游戏应该继承此类并添加 GameplayAttributeData 类型的字段来表示具体的属性（如 Health, Mana 等）。
/// 继承 MonoBehaviour 以便挂载到 GameObject 上。
/// 参考 UAttributeSet
/// </summary>
public abstract class AttributeSet : MonoBehaviour
{
    /// <summary>
    /// 获取拥有此 AttributeSet 的 ASC
    /// </summary>
    public AbilitySystemComponent GetOwningAbilitySystemComponent()
    {
        return GetComponent<AbilitySystemComponent>();
    }

    /// <summary>
    /// 获取拥有此 AttributeSet 的 Actor
    /// </summary>
    public GameObject GetOwningActor()
    {
        return gameObject;
    }

    /// <summary>
    /// 在属性值即将发生改变时调用（CurrentValue）。
    /// 通常用于限制数值范围（Clamping），例如保持血量在 0 到 MaxHealth 之间。
    /// 注意：NewValue 是引用传递，可以在此处修改最终应用的值。
    /// </summary>
    public virtual void PreAttributeChange(GameplayAttribute Attribute, ref float NewValue)
    {
    }

    /// <summary>
    /// 在属性值发生改变后调用（CurrentValue）。
    /// 可以用于触发 UI 更新或其他逻辑。
    /// </summary>
    public virtual void PostAttributeChange(GameplayAttribute Attribute, float OldValue, float NewValue)
    {
    }

    /// <summary>
    /// 在属性的 BaseValue 即将发生改变时调用。
    /// 类似于 PreAttributeChange，但针对的是永久的基础值。
    /// </summary>
    public virtual void PreAttributeBaseChange(GameplayAttribute Attribute, ref float NewValue)
    {
    }

    /// <summary>
    /// 在属性的 BaseValue 发生改变后调用。
    /// </summary>
    public virtual void PostAttributeBaseChange(GameplayAttribute Attribute, float OldValue, float NewValue)
    {
    }

    /// <summary>
    /// 在 GameplayEffect 执行修改属性之前调用。
    /// 返回 true 继续执行修改，返回 false 阻止修改。
    /// 注意：这通常只在 Instant 类型的 GE 修改 BaseValue 时调用。
    /// </summary>
    public virtual bool PreGameplayEffectExecute(GameplayEffectModCallbackData Data)
    {
        return true;
    }

    /// <summary>
    /// 在 GameplayEffect 执行修改属性之后调用。
    /// 这是处理“元属性”（Meta Attributes）如 Damage 的理想位置。
    /// </summary>
    public virtual void PostGameplayEffectExecute(GameplayEffectModCallbackData Data)
    {
    }
}
