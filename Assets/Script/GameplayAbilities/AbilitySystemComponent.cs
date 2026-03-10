using System;
using System.Collections.Generic;
using UnityEngine;

public class AbilitySystemComponent
{
    private GameplayTagCountContainer GameplayTagCountContainer = new GameplayTagCountContainer();

    public bool HasMatchingGameplayTag(GameplayTag TagToCheck)
	{
		return GameplayTagCountContainer.HasMatchingGameplayTag(TagToCheck);
	}

    public bool HasAllMatchingGameplayTags(GameplayTagContainer TagContainer)
	{
		return GameplayTagCountContainer.HasAllMatchingGameplayTags(TagContainer);
	}

    public bool HasAnyMatchingGameplayTags(GameplayTagContainer TagContainer)
	{
		return GameplayTagCountContainer.HasAnyMatchingGameplayTags(TagContainer);
	}

    public void GetOwnedGameplayTags(GameplayTagContainer TagContainer)
	{
        if (TagContainer == null) return;
		TagContainer.Reset();
		TagContainer.AppendTags(GetOwnedGameplayTags());
	}

    public GameplayTagContainer GetOwnedGameplayTags()
	{
		return GameplayTagCountContainer.GetExplicitGameplayTags();
	}

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int GetTagCount(GameplayTag TagToCheck)
	{
		return GameplayTagCountContainer.GetTagCount(TagToCheck);
	}

    public void SetTagMapCount(GameplayTag Tag, int NewCount, GameplayTagReplicationState RepState = GameplayTagReplicationState.None)
    {
        int CurrentCount = GameplayTagCountContainer.GetExplicitTagCount(Tag);
        int Delta = NewCount - CurrentCount;
        if (Delta != 0)
        {
            UpdateTagMap(Tag, Delta, RepState);
        }
    }

    public void UpdateTagMap(GameplayTag Tag, int CountDelta, GameplayTagReplicationState RepState = GameplayTagReplicationState.None)
    {
        UpdateTagMapSingle_Internal(Tag, CountDelta, RepState);
    }

    public void UpdateTagMap(GameplayTagContainer Container, int CountDelta, GameplayTagReplicationState RepState = GameplayTagReplicationState.None)
    {
        if (Container != null && !Container.IsEmpty())
        {
            UpdateTagMap_Internal(Container, CountDelta, RepState);
        }
    }

    public void AddLooseGameplayTag(GameplayTag GameplayTag, int Count = 1)
    {
        UpdateTagMap(GameplayTag, Count, GameplayTagReplicationState.None);
    }

    public void AddLooseGameplayTags(GameplayTagContainer GameplayTags, int Count = 1)
    {
        UpdateTagMap(GameplayTags, Count, GameplayTagReplicationState.None);
    }

    public void RemoveLooseGameplayTag(GameplayTag GameplayTag, int Count = 1)
    {
        UpdateTagMap(GameplayTag, -Count, GameplayTagReplicationState.None);
    }

    public void RemoveLooseGameplayTags(GameplayTagContainer GameplayTags, int Count = 1)
    {
        UpdateTagMap(GameplayTags, -Count, GameplayTagReplicationState.None);
    }

    public void SetLooseGameplayTagCount(GameplayTag GameplayTag, int NewCount)
    {
        SetTagMapCount(GameplayTag, NewCount, GameplayTagReplicationState.None);
    }

    public virtual void OnTagUpdated(GameplayTag Tag, bool bTagExists)
    {
        // 供子类重写或广播事件
    }

    private void UpdateTagMapSingle_Internal(GameplayTag Tag, int CountDelta, GameplayTagReplicationState RepState = GameplayTagReplicationState.None)
    {
        if (CountDelta > 0)
        {
            if (GameplayTagCountContainer.UpdateTagCount(Tag, CountDelta, RepState))
            {
                OnTagUpdated(Tag, true);
            }
        }
        else if (CountDelta < 0)
        {
            List<GameplayTagCountContainer.DeferredTagChangeDelegate> DeferredTagChangeDelegates = new List<GameplayTagCountContainer.DeferredTagChangeDelegate>();
            if (GameplayTagCountContainer.UpdateTagCount_DeferredParentRemoval(Tag, CountDelta, DeferredTagChangeDelegates))
            {
                GameplayTagCountContainer.FillParentTags();
                OnTagUpdated(Tag, false);

                foreach (var Delegate in DeferredTagChangeDelegates)
                {
                    Delegate.Invoke();
                }
            }
        }
    }

    private void UpdateTagMap_Internal(GameplayTagContainer Container, int CountDelta, GameplayTagReplicationState RepState = GameplayTagReplicationState.None)
    {
        if (CountDelta > 0)
        {
            foreach (var Tag in Container.GameplayTags)
            {
                if (GameplayTagCountContainer.UpdateTagCount(Tag, CountDelta, RepState))
                {
                    OnTagUpdated(Tag, true);
                }
            }
        }
        else if (CountDelta < 0)
        {
            List<GameplayTag> RemovedTags = new List<GameplayTag>();
            List<GameplayTagCountContainer.DeferredTagChangeDelegate> DeferredTagChangeDelegates = new List<GameplayTagCountContainer.DeferredTagChangeDelegate>();

            foreach (var Tag in Container.GameplayTags)
            {
                if (GameplayTagCountContainer.UpdateTagCount_DeferredParentRemoval(Tag, CountDelta, DeferredTagChangeDelegates))
                {
                    RemovedTags.Add(Tag);
                }
            }

            if (RemovedTags.Count > 0)
            {
                GameplayTagCountContainer.FillParentTags();
                
                foreach (var Delegate in DeferredTagChangeDelegates)
                {
                    Delegate.Invoke();
	}

                foreach (var Tag in RemovedTags)
                {
                    OnTagUpdated(Tag, false);
                }
            }
        }
    }
}
