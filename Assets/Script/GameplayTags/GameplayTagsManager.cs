using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using static UnityEditorInternal.ReorderableList;


public class GameplayTagNode
{
    protected string TagName;
    protected string FullTagName;
    public bool bIsExplicitTag;
    protected GameplayTagNode ParentNode;
    protected List<GameplayTagNode> ChildNodes = new List<GameplayTagNode>();
    GameplayTagContainer CompleteTagWithParents = new GameplayTagContainer();

    public GameplayTagNode()
    {
    }

    public GameplayTagNode(string InTag, string InFullTag, GameplayTagNode InParentNode, bool InIsExplicitTag)
    {
        TagName = InTag;
        FullTagName = InFullTag;
        ParentNode = InParentNode;
        bIsExplicitTag = InIsExplicitTag;

        CompleteTagWithParents.GameplayTags.Add(new GameplayTag(InFullTag));
        if (ParentNode != null)
        {
            GameplayTagContainer ParentContainer = ParentNode.GetSingleTagContainer();
            CompleteTagWithParents.ParentTags.Add(ParentContainer.GameplayTags[0]);
            CompleteTagWithParents.ParentTags.AddRange(ParentContainer.ParentTags);
        }
    }

    public string GetSimpleTagName() => TagName;
    public GameplayTagContainer GetSingleTagContainer() => CompleteTagWithParents;

    public List<GameplayTagNode> GetChildTagNodes() => ChildNodes;

    internal GameplayTag GetCompleteTag()
    {
        return CompleteTagWithParents.Num() > 0 ? CompleteTagWithParents.GameplayTags[0] : GameplayTag.EmptyTag;
    }
}

public class GameplayTagsManager:MonoBehaviour
{
    private static GameplayTagsManager SingletonManager;

    public static GameplayTagsManager Get()
    {
        if (SingletonManager == null)
        {
            InitializeManager();
        }
        return SingletonManager;
    }

    private static void InitializeManager()
    {
        Assert.IsNull(SingletonManager, "GameplayTagsManager already initialized!");

        GameObject go = new GameObject("GameplayTagsManager");
        SingletonManager = go.AddComponent<GameplayTagsManager>();

        SingletonManager.ConstructGameplayTagTree();
    }


    GameplayTagNode GameplayRootTag;
    private Dictionary<GameplayTag, GameplayTagNode> GameplayTagNodeMap = new Dictionary<GameplayTag, GameplayTagNode>();

    private void ConstructGameplayTagTree()
    {
        if (null == GameplayRootTag)
        {
            GameplayRootTag = new GameplayTagNode();

            GameplayTagsSettings Default = AssetDatabase.LoadAssetAtPath<GameplayTagsSettings>("Assets/Config/GameplayTagsSettings.asset");
            foreach (string TableRow in Default.GameplayTagList)
            {
                AddTagTableRow(TableRow, "Default");
            }
        }
    }

    private struct RequiredTag
    {
        public string ShortTagName;
        public string FullTagName;
        public bool bIsExplicitTag;
    };

    private void AddTagTableRow(string InTagRow, string SourceName)
    {
        GameplayTagNode CurNode = GameplayRootTag;

        string OriginalTagNmae = InTagRow;
#if UNITY_EDITOR
        if (!IsValidGameplayTagString(InTagRow, out string ErrorString))
        {
            Debug.LogError($"Invalid Gameplay Tag: {InTagRow}. Error: {ErrorString}");
            return;
        }
#endif

        Stack<RequiredTag> RequiredTags = new Stack<RequiredTag>();
        string Remainder = OriginalTagNmae;

        while (!string.IsNullOrEmpty(Remainder))
        {
            string CurrentFullTag = Remainder;
            string SubTag;

            int LastPeriodIdx = Remainder.LastIndexOf('.');
            if (LastPeriodIdx != -1)
            {
                SubTag = Remainder.Substring(LastPeriodIdx + 1);
                Remainder = Remainder.Substring(0, LastPeriodIdx);
            }
            else
            {
                SubTag = Remainder;
                Remainder = string.Empty;
            }

            if (string.IsNullOrEmpty(SubTag)) continue;

            bool bIsExplicitTag = (CurrentFullTag.Length == OriginalTagNmae.Length);

#if !UNITY_EDITOR
            if (!bIsExplicitTag)
            {
                if (GameplayTagNodeMap.TryGetValue(CurrentFullTag, out var FoundNode))
                {
                    CurNode = FoundNode;
                    break;
                }
            }
#endif
            RequiredTags.Push(new RequiredTag
            {
                ShortTagName = SubTag,
                FullTagName = CurrentFullTag,
                bIsExplicitTag = bIsExplicitTag
            });
        }

        while (RequiredTags.Count > 0)
        {
            RequiredTag CurrentTag = RequiredTags.Pop();

            List<GameplayTagNode> ChildTags = CurNode.GetChildTagNodes();
            int InsertionIdx = InsertTagIntoNodeArray(CurrentTag.ShortTagName, CurrentTag.FullTagName, CurNode, ChildTags, SourceName, CurrentTag.bIsExplicitTag);
            CurNode = ChildTags[InsertionIdx];
        }
    }

    private int InsertTagIntoNodeArray(string Tag, string FullTag, GameplayTagNode ParentNode, List<GameplayTagNode> NodeArray, string SourceName, bool bIsExplicitTag)
    {
        int FoundNodeIdx = -1;
        int WhereToInsert = -1;

        // 模拟 Algo::LowerBoundBy，保持数组按名称排序
        int Low = 0;
        int High = NodeArray.Count;
        while (Low < High)
        {
            int Mid = Low + ((High - Low) >> 1);
            if (string.Compare(NodeArray[Mid].GetSimpleTagName(), Tag, StringComparison.Ordinal) < 0)
        {
                Low = Mid + 1;
            }
            else
            {
                High = Mid;
            }
        }

        int LowerBoundIndex = Low;

        if (LowerBoundIndex < NodeArray.Count)
        {
            GameplayTagNode CurrNode = NodeArray[LowerBoundIndex];
            if (CurrNode.GetSimpleTagName() == Tag)
            {
                FoundNodeIdx = LowerBoundIndex;
                // 如果是显式添加，更新现有节点的显式标记
                if (bIsExplicitTag)
                {
                    CurrNode.bIsExplicitTag = CurrNode.bIsExplicitTag || bIsExplicitTag;
                }
            }
            else
            {
                WhereToInsert = LowerBoundIndex;
            }
        }

        if (FoundNodeIdx == -1)
        {
            if (WhereToInsert == -1)
            {
                WhereToInsert = NodeArray.Count;
            }

            GameplayTagNode TagNode = new GameplayTagNode(Tag, FullTag, ParentNode != GameplayRootTag ? ParentNode : null, bIsExplicitTag);

            NodeArray.Insert(WhereToInsert, TagNode);
            FoundNodeIdx = WhereToInsert;

            GameplayTag GameplayTag = TagNode.GetCompleteTag();
            GameplayTagNodeMap.Add(GameplayTag, TagNode);
        }

        return FoundNodeIdx;
    }

#if UNITY_EDITOR
    public static bool IsValidGameplayTagString(string TagString, out string OutError)
    {
        OutError = string.Empty;
        if (string.IsNullOrEmpty(TagString))
        {
            OutError = "Tag string is empty.";
            return false;
        }

        bool bLastCharWasDot = false;
        if (TagString[0] == '.')
        {
            OutError = "Tag string cannot start with a dot.";
            return false;
        }
        if (TagString[TagString.Length - 1] == '.')
        {
            OutError = "Tag string cannot end with a dot.";
            return false;
        }

        for (int i = 0; i < TagString.Length; ++i)
        {
            char c = TagString[i];
            if (c == '.')
            {
                if (bLastCharWasDot)
                {
                    OutError = "Tag string cannot contain consecutive dots.";
                    return false;
                }
                bLastCharWasDot = true;
            }
            else
            {
                // Unreal 允许字母、数字、下划线和连字符
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                {
                    OutError = $"Tag string contains invalid character: {c}";
                    return false;
                }
                bLastCharWasDot = false;
            }
        }

        return true;
    }
#endif

    public GameplayTag RequestGameplayTag(string TagName, bool ErrorIfNotFound = true)
    {
        if (string.IsNullOrEmpty(TagName))
        {
            return GameplayTag.EmptyTag;
        }

        GameplayTag PossibleTag = new GameplayTag(TagName);
        if (GameplayTagNodeMap.ContainsKey(PossibleTag))
        {
            return PossibleTag;
            }

        if (ErrorIfNotFound)
        {
            Debug.LogError($"Requested Gameplay Tag {TagName} not found!");
        }

        return GameplayTag.EmptyTag;
    }

    public GameplayTagNode FindTagNode(GameplayTag Tag)
    {
        if (GameplayTagNodeMap.TryGetValue(Tag, out var Node))
        {
            return Node;
        }
        return null;
    }

    public GameplayTagContainer GetSingleTagContainer(GameplayTag Tag)
    {
        var Node = FindTagNode(Tag);
        return Node != null ? Node.GetSingleTagContainer() : null;
    }

}
