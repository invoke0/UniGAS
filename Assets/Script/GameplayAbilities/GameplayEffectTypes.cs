using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameplayTagReplicationState : byte
{
    None,               // 标签不进行同步
    TagOnly,            // 标签同步给所有人，但不包含计数 (Minimal Replication)
    CountToOwner,       // 标签同步给所有人，但计数仅同步给拥有者
    TagAndCountToAll    // 标签和计数都同步给所有人
}

public struct GameplayTagCountItem : IEquatable<GameplayTagCountItem>
{
    public GameplayTag Tag;
    public int Count;
    public GameplayTagReplicationState ReplicationState;

    public GameplayTagCountItem(GameplayTag InTag, int InCount, GameplayTagReplicationState InRepState = GameplayTagReplicationState.None)
    {
        Tag = InTag;
        Count = InCount;
        ReplicationState = InRepState;
    }

    public bool Equals(GameplayTagCountItem other) => Tag.Equals(other.Tag);
    public override bool Equals(object obj) => obj is GameplayTagCountItem other && Equals(other);
    public override int GetHashCode() => Tag.GetHashCode();
}

public class GameplayTagCountContainer
{
    private List<GameplayTagCountItem> Items = new List<GameplayTagCountItem>();
    private Dictionary<GameplayTag, int> GameplayTagCountMap = new Dictionary<GameplayTag, int>();
    private GameplayTagContainer ExplicitTags = new GameplayTagContainer();

    // 委托定义，模拟 UE 的 FDeferredTagChangeDelegate
    public delegate void DeferredTagChangeDelegate();

    // 委托定义，模拟 UE 的 FOnGameplayEffectTagCountChanged
    public delegate void OnGameplayEffectTagCountChanged(GameplayTag Tag, int NewCount);
    private Dictionary<GameplayTag, OnGameplayEffectTagCountChanged> OnNewOrRemoveDelegates = new Dictionary<GameplayTag, OnGameplayEffectTagCountChanged>();
    private Dictionary<GameplayTag, OnGameplayEffectTagCountChanged> OnAnyChangeDelegates = new Dictionary<GameplayTag, OnGameplayEffectTagCountChanged>();
    public event OnGameplayEffectTagCountChanged OnAnyTagChangeDelegate;

    public bool HasMatchingGameplayTag(GameplayTag TagToCheck)
    {
        return GetTagCount(TagToCheck) > 0;
    }

    public bool HasAllMatchingGameplayTags(GameplayTagContainer TagContainer)
    {
        if (TagContainer == null || TagContainer.IsEmpty()) return true;

        foreach (var Tag in TagContainer.GameplayTags)
        {
            if (GetTagCount(Tag) <= 0) return false;
        }
        return true;
    }

    public bool HasAnyMatchingGameplayTags(GameplayTagContainer TagContainer)
    {
        if (TagContainer == null || TagContainer.IsEmpty()) return false;

        foreach (var Tag in TagContainer.GameplayTags)
        {
            if (GetTagCount(Tag) > 0) return true;
        }
        return false;
    }

    public void UpdateTagCount(GameplayTagContainer Container, int CountDelta)
    {
        if (CountDelta != 0)
        {
            bool bUpdatedAny = false;
            List<DeferredTagChangeDelegate> DeferredTagChangeDelegates = new List<DeferredTagChangeDelegate>();
            
            foreach (var Tag in Container.GameplayTags)
            {
                bUpdatedAny |= UpdateTagMapDeferredParentRemoval_Internal(Tag, CountDelta, DeferredTagChangeDelegates, GameplayTagReplicationState.None);
            }

            if (bUpdatedAny && CountDelta < 0)
            {
                ExplicitTags.FillParentTags();
            }

            foreach (var Delegate in DeferredTagChangeDelegates)
            {
                Delegate.Invoke();
            }
        }
    }

    public bool UpdateTagCount(GameplayTag Tag, int CountDelta, GameplayTagReplicationState RepState = GameplayTagReplicationState.None)
    {
        if (!Tag.IsValid() || CountDelta == 0) return false;

        return UpdateTagMap_Internal(Tag, CountDelta, RepState);
    }

    public bool UpdateTagCount_DeferredParentRemoval(GameplayTag Tag, int CountDelta, List<DeferredTagChangeDelegate> DeferredTagChangeDelegates)
    {
        if (CountDelta != 0)
        {
            return UpdateTagMapDeferredParentRemoval_Internal(Tag, CountDelta, DeferredTagChangeDelegates, GameplayTagReplicationState.None);
        }
        return false;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int GetTagCount(GameplayTag Tag)
    {
        if (GameplayTagCountMap.TryGetValue(Tag, out int Count))
        {
            return Count;
        }
        return 0;
    }

    public int GetExplicitTagCount(GameplayTag Tag)
    {
        int idx = Items.FindIndex(x => x.Tag == Tag);
        return idx != -1 ? Items[idx].Count : 0;
    }

    private bool UpdateTagMap_Internal(GameplayTag Tag, int CountDelta, GameplayTagReplicationState TagRepState)
    {
        if (!Tag.IsValid() || CountDelta == 0) return false;

        if (!UpdateExplicitTags(Tag, CountDelta, false, TagRepState))
        {
            return false;
        }

        List<DeferredTagChangeDelegate> DeferredTagChangeDelegates = new List<DeferredTagChangeDelegate>();
        bool bSignificantChange = GatherTagChangeDelegates(Tag, CountDelta, DeferredTagChangeDelegates, TagRepState);
        
        foreach (var Delegate in DeferredTagChangeDelegates)
        {
            Delegate.Invoke();
        }

        return bSignificantChange;
    }

    private bool UpdateTagMapDeferredParentRemoval_Internal(GameplayTag Tag, int CountDelta, List<DeferredTagChangeDelegate> DeferredTagChangeDelegates, GameplayTagReplicationState TagRepState)
    {
        if (!UpdateExplicitTags(Tag, CountDelta, true, TagRepState))
        {
            return false;
        }

        return GatherTagChangeDelegates(Tag, CountDelta, DeferredTagChangeDelegates, TagRepState);
    }

    private bool UpdateExplicitTags(GameplayTag Tag, int CountDelta, bool bDeferParentTagsOnRemove, GameplayTagReplicationState TagRepState)
    {
        int TagCountIndex = Items.FindIndex(x => x.Tag == Tag);

        if (TagCountIndex != -1)
        {
            var TagCountItem = Items[TagCountIndex];
            bool RepTypeMatch = TagCountItem.ReplicationState == TagRepState;
            
            if (!RepTypeMatch && CountDelta > 0)
            {
                // 提升同步状态到更高级别
                TagCountItem.ReplicationState = TagRepState > TagCountItem.ReplicationState ? TagRepState : TagCountItem.ReplicationState;
                TagRepState = TagCountItem.ReplicationState;
            }

            TagCountItem.Count += CountDelta;
            Items[TagCountIndex] = TagCountItem; // 结构体需要重新赋值

            if (TagCountItem.Count <= 0)
            {
                Items.RemoveAt(TagCountIndex);
                ExplicitTags.RemoveTag(Tag, bDeferParentTagsOnRemove);
            }
        }
        else if (CountDelta > 0)
        {
            Items.Add(new GameplayTagCountItem(Tag, CountDelta, TagRepState));
            ExplicitTags.AddTag(Tag);
        }
        else
        {
            Debug.LogWarning($"Attempted to remove tag: {Tag} from tag count container, but it is not explicitly in the container!");
            return false;
        }

        return true;
    }

    private bool GatherTagChangeDelegates(GameplayTag Tag, int CountDelta, List<DeferredTagChangeDelegate> TagChangeDelegates, GameplayTagReplicationState TagRepState)
    {
        var SingleContainer = GameplayTagsManager.Get().GetSingleTagContainer(Tag);
        if (SingleContainer == null) return false;

        bool CreatedSignificantChange = false;

        // 包含自己和所有父标签
        List<GameplayTag> AllTags = new List<GameplayTag>();
        AllTags.Add(Tag);
        AllTags.AddRange(SingleContainer.ParentTags);

        foreach (var CurTag in AllTags)
        {
            int OldCount = GetTagCount(CurTag);
            int NewTagCount = Math.Max(OldCount + CountDelta, 0);
            
            GameplayTagCountMap[CurTag] = NewTagCount;

            bool SignificantChange = (OldCount == 0 || NewTagCount == 0);
            CreatedSignificantChange |= SignificantChange;

            if (SignificantChange)
            {
                TagChangeDelegates.Add(() => {
                    OnAnyTagChangeDelegate?.Invoke(CurTag, NewTagCount);
                });
            }

            if (OnAnyChangeDelegates.TryGetValue(CurTag, out var anyChange))
            {
                TagChangeDelegates.Add(() => {
                    anyChange.Invoke(CurTag, NewTagCount);
                });
            }

            if (SignificantChange && OnNewOrRemoveDelegates.TryGetValue(CurTag, out var newOrRemove))
            {
                TagChangeDelegates.Add(() => {
                    newOrRemove.Invoke(CurTag, NewTagCount);
                });
            }
        }

        return CreatedSignificantChange;
    }

    public void FillParentTags()
    {
        ExplicitTags.FillParentTags();
    }

    public void RegisterGameplayTagEvent(GameplayTag Tag, OnGameplayEffectTagCountChanged Callback, bool bAnyChange = false)
    {
        if (bAnyChange)
        {
            if (!OnAnyChangeDelegates.ContainsKey(Tag)) OnAnyChangeDelegates[Tag] = null;
            OnAnyChangeDelegates[Tag] += Callback;
        }
        else
        {
            if (!OnNewOrRemoveDelegates.ContainsKey(Tag)) OnNewOrRemoveDelegates[Tag] = null;
            OnNewOrRemoveDelegates[Tag] += Callback;
        }
    }

    public GameplayTagContainer GetExplicitGameplayTags() => ExplicitTags;
}
