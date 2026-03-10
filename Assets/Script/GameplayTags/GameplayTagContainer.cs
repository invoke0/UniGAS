using System;
using System.Collections.Generic;
using System.Linq;

public struct GameplayTag : IEquatable<GameplayTag>
{
    public string TagName;

    public static readonly GameplayTag EmptyTag = new GameplayTag(string.Empty);

    public GameplayTag(string tagName)
    {
        TagName = tagName;
    }

    public static GameplayTag RequestGameplayTag(string TagName, bool ErrorIfNotFound = true)
    {
        return GameplayTagsManager.Get().RequestGameplayTag(TagName, ErrorIfNotFound);
    }

    public bool IsValid() => !string.IsNullOrEmpty(TagName);

    public override string ToString() => TagName;

    public bool Equals(GameplayTag other)
    {
        return TagName == other.TagName;
    }

    public override bool Equals(object obj)
    {
        return obj is GameplayTag other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (TagName != null ? TagName.GetHashCode() : 0);
    }

    public static bool operator ==(GameplayTag left, GameplayTag right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GameplayTag left, GameplayTag right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// 检查此标签是否匹配另一个标签（包括层级关系）。
    /// 例如：A.B 匹配 A
    /// </summary>
    public bool MatchesTag(GameplayTag TagToCheck)
    {
        var TagNode = GameplayTagsManager.Get().FindTagNode(this);
        if (TagNode != null)
        {
            return TagNode.GetSingleTagContainer().HasTag(TagToCheck);
        }
        return false;
    }

    /// <summary>
    /// 精确匹配，不考虑层级
    /// </summary>
    public bool MatchesTagExact(GameplayTag TagToCheck)
    {
        if (!TagToCheck.IsValid()) return false;
        return this == TagToCheck;
    }

    /// <summary>
    /// 检查此标签是否匹配容器中的任何一个标签（考虑层级）
    /// </summary>
    public bool MatchesAny(GameplayTagContainer ContainerToCheck)
    {
        var TagNode = GameplayTagsManager.Get().FindTagNode(this);

        if (TagNode != null)
        {
            return TagNode.GetSingleTagContainer().HasAny(ContainerToCheck);
        }

        return false;
    }

    /// <summary>
    /// 检查此标签是否匹配容器中的任何一个标签（精确匹配）
    /// </summary>
    public bool MatchesAnyExact(GameplayTagContainer ContainerToCheck)
    {
        var TagNode = GameplayTagsManager.Get().FindTagNode(this);

        if (TagNode != null)
        {
            return TagNode.GetSingleTagContainer().HasAnyExact(ContainerToCheck);
        }

        return false;
    }
}

public enum EGameplayTagQueryExprType : byte
{
    Undefined = 0,
    AnyTagsMatch,
    AllTagsMatch,
    NoTagsMatch,
    AnyExprMatch,
    AllExprMatch,
    NoExprMatch,
    AnyTagsExactMatch,
    AllTagsExactMatch
}

public enum GameplayTagQueryStreamVersion : byte
{
    InitialVersion = 0,
    LatestVersion = InitialVersion
}

// 内部求值器
internal class QueryEvaluator
{
    private GameplayTagQuery Query;
    private int CurStreamIdx;
    private bool bReadError;
    private byte[] Stream;

    public QueryEvaluator(GameplayTagQuery Q)
    {
        Query = Q;
        CurStreamIdx = 0;
        bReadError = false;
        Stream = Q.GetQueryTokenStream();
    }

    private byte GetToken()
    {
        if (Stream != null && CurStreamIdx < Stream.Length)
        {
            return Stream[CurStreamIdx++];
        }
        bReadError = true;
        return 0;
    }

    public bool Eval(GameplayTagContainer Tags)
    {
        if (Stream == null || Stream.Length == 0) return false;

        CurStreamIdx = 0;
        byte Version = GetToken();
        if (bReadError) return false;

        byte bHasRootExpression = GetToken();
        if (!bReadError && bHasRootExpression != 0)
        {
            return EvalExpr(Tags);
        }
        return false;
    }

    private bool EvalExpr(GameplayTagContainer Tags, bool bSkip = false)
    {
        EGameplayTagQueryExprType ExprType = (EGameplayTagQueryExprType)GetToken();
        if (bReadError) return false;

        switch (ExprType)
        {
            case EGameplayTagQueryExprType.AnyTagsMatch:
                return EvalAnyTagsMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.AllTagsMatch:
                return EvalAllTagsMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.NoTagsMatch:
                return EvalNoTagsMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.AnyExprMatch:
                return EvalAnyExprMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.AllExprMatch:
                return EvalAllExprMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.NoExprMatch:
                return EvalNoExprMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.AnyTagsExactMatch:
                return EvalAnyTagsExactMatch(Tags, bSkip);
            case EGameplayTagQueryExprType.AllTagsExactMatch:
                return EvalAllTagsExactMatch(Tags, bSkip);
        }
        return false;
    }

    private bool EvalAnyTagsMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumTags = GetToken();
        bool bResult = false;
        for (int i = 0; i < NumTags; i++)
        {
            int TagIdx = GetToken();
            if (!bSkip && !bResult)
            {
                if (Tags.HasTag(Query.GetTagFromIndex(TagIdx))) bResult = true;
            }
        }
        return bResult;
    }

    private bool EvalAllTagsMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumTags = GetToken();
        bool bResult = true;
        for (int i = 0; i < NumTags; i++)
        {
            int TagIdx = GetToken();
            if (!bSkip && bResult)
            {
                if (!Tags.HasTag(Query.GetTagFromIndex(TagIdx))) bResult = false;
            }
        }
        return bResult;
    }

    private bool EvalNoTagsMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumTags = GetToken();
        bool bResult = true;
        for (int i = 0; i < NumTags; i++)
        {
            int TagIdx = GetToken();
            if (!bSkip && bResult)
            {
                if (Tags.HasTag(Query.GetTagFromIndex(TagIdx))) bResult = false;
            }
        }
        return bResult;
    }

    private bool EvalAnyTagsExactMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumTags = GetToken();
        bool bResult = false;
        for (int i = 0; i < NumTags; i++)
        {
            int TagIdx = GetToken();
            if (!bSkip && !bResult)
            {
                if (Tags.HasTagExact(Query.GetTagFromIndex(TagIdx))) bResult = true;
            }
        }
        return bResult;
    }

    private bool EvalAllTagsExactMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumTags = GetToken();
        bool bResult = true;
        for (int i = 0; i < NumTags; i++)
        {
            int TagIdx = GetToken();
            if (!bSkip && bResult)
            {
                if (!Tags.HasTagExact(Query.GetTagFromIndex(TagIdx))) bResult = false;
            }
        }
        return bResult;
    }

    private bool EvalAnyExprMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumExprs = GetToken();
        bool bResult = false;
        for (int i = 0; i < NumExprs; i++)
        {
            bool bExprResult = EvalExpr(Tags, bSkip || bResult);
            if (!bSkip && bExprResult) bResult = true;
        }
        return bResult;
    }

    private bool EvalAllExprMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumExprs = GetToken();
        bool bResult = true;
        for (int i = 0; i < NumExprs; i++)
        {
            bool bExprResult = EvalExpr(Tags, bSkip || !bResult);
            if (!bSkip && !bExprResult) bResult = false;
        }
        return bResult;
    }

    private bool EvalNoExprMatch(GameplayTagContainer Tags, bool bSkip)
    {
        int NumExprs = GetToken();
        bool bResult = true;
        for (int i = 0; i < NumExprs; i++)
        {
            bool bExprResult = EvalExpr(Tags, bSkip || !bResult);
            if (!bSkip && bExprResult) bResult = false;
        }
        return bResult;
    }
}

public class GameplayTagQuery
{
    private int TokenStreamVersion;
    private GameplayTag[] TagDictionary;
    private byte[] QueryTokenStream;
    private string UserDescription;
    private string AutoDescription;

    public static readonly GameplayTagQuery EmptyQuery = new GameplayTagQuery();

    public GameplayTagQuery()
    {
        TokenStreamVersion = (int)GameplayTagQueryStreamVersion.LatestVersion;
        TagDictionary = Array.Empty<GameplayTag>();
        QueryTokenStream = Array.Empty<byte>();
    }

    public byte[] GetQueryTokenStream() => QueryTokenStream;
    public GameplayTag GetTagFromIndex(int index) => TagDictionary[index];

    public bool Matches(GameplayTagContainer Tags)
    {
        if (IsEmpty())
        {
            return false;
        }

        QueryEvaluator QE = new QueryEvaluator(this);
        return QE.Eval(Tags);
    }

    public bool IsEmpty()
    {
        return QueryTokenStream == null || QueryTokenStream.Length == 0;
    }

    public void Clear()
    {
        TokenStreamVersion = (int)GameplayTagQueryStreamVersion.LatestVersion;
        TagDictionary = Array.Empty<GameplayTag>();
        QueryTokenStream = Array.Empty<byte>();
        UserDescription = string.Empty;
        AutoDescription = string.Empty;
    }

    public GameplayTag[] GetGameplayTagArray() => TagDictionary;

    // 静态工厂方法 - 快捷创建常用查询
    public static GameplayTagQuery MakeQuery_MatchAnyTags(GameplayTagContainer InTags)
    {
        var expr = FrameObjectPool<GameplayTagQueryExpression>.Claim().AnyTagsMatch().AddTags(InTags);
        return BuildQuery(expr);
    }

    public static GameplayTagQuery MakeQuery_MatchAllTags(GameplayTagContainer InTags)
    {
        var expr = FrameObjectPool<GameplayTagQueryExpression>.Claim().AllTagsMatch().AddTags(InTags);
        return BuildQuery(expr);
    }

    public static GameplayTagQuery MakeQuery_MatchNoTags(GameplayTagContainer InTags)
    {
        var expr = FrameObjectPool<GameplayTagQueryExpression>.Claim().NoTagsMatch().AddTags(InTags);
        return BuildQuery(expr);
    }

    public static GameplayTagQuery MakeQuery_ExactMatchAnyTags(GameplayTagContainer InTags)
    {
        var expr = FrameObjectPool<GameplayTagQueryExpression>.Claim().AnyTagsExactMatch().AddTags(InTags);
        return BuildQuery(expr);
    }

    public static GameplayTagQuery MakeQuery_ExactMatchAllTags(GameplayTagContainer InTags)
    {
        var expr = FrameObjectPool<GameplayTagQueryExpression>.Claim().AllTagsExactMatch().AddTags(InTags);
        return BuildQuery(expr);
    }

    public static GameplayTagQuery MakeQuery_MatchTag(GameplayTag InTag)
    {
        var expr = FrameObjectPool<GameplayTagQueryExpression>.Claim().AllTagsMatch().AddTag(InTag);
        return BuildQuery(expr);
    }

    private static GameplayTagQuery BuildQuery(GameplayTagQueryExpression RootExpr)
    {
        GameplayTagQuery Q = new GameplayTagQuery();
        Q.Build(RootExpr);
        // 构建完成后，回收表达式树
        FrameObjectPool<GameplayTagQueryExpression>.Release(RootExpr);
        return Q;
    }

    private static readonly List<byte> SharedTempTokenStream = new List<byte>(128);
    private static readonly List<GameplayTag> SharedTempTagDictionary = new List<GameplayTag>(32);

    private void Build(GameplayTagQueryExpression RootExpr)
    {
        TokenStreamVersion = (int)GameplayTagQueryStreamVersion.LatestVersion;
        
        lock (SharedTempTokenStream)
        {
            SharedTempTokenStream.Clear();
            SharedTempTagDictionary.Clear();

            SharedTempTokenStream.Add((byte)TokenStreamVersion);
            SharedTempTokenStream.Add(1); // HasRoot
            RootExpr.EmitTokens(SharedTempTokenStream, SharedTempTagDictionary);

            // 构建完成后，转换为数组，确保运行时的高效访问和内存连续性
            QueryTokenStream = SharedTempTokenStream.ToArray();
            TagDictionary = SharedTempTagDictionary.ToArray();
        }
    }
}


public class GameplayTagQueryExpression : IFramePooledObject
{
    public EGameplayTagQueryExprType ExprType = EGameplayTagQueryExprType.Undefined;
    public List<GameplayTag> TagSet = new List<GameplayTag>();
    public List<GameplayTagQueryExpression> ExprSet = new List<GameplayTagQueryExpression>();

    public void OnEnterPool()
    {
        ExprType = EGameplayTagQueryExprType.Undefined;
        TagSet.Clear();
        // 递归释放子表达式
        foreach (var expr in ExprSet)
        {
            if (expr != null)
            {
                FrameObjectPool<GameplayTagQueryExpression>.Release(expr);
            }
        }
        ExprSet.Clear();
    }

    public GameplayTagQueryExpression AnyTagsMatch() { ExprType = EGameplayTagQueryExprType.AnyTagsMatch; return this; }
    public GameplayTagQueryExpression AllTagsMatch() { ExprType = EGameplayTagQueryExprType.AllTagsMatch; return this; }
    public GameplayTagQueryExpression NoTagsMatch() { ExprType = EGameplayTagQueryExprType.NoTagsMatch; return this; }
    public GameplayTagQueryExpression AnyTagsExactMatch() { ExprType = EGameplayTagQueryExprType.AnyTagsExactMatch; return this; }
    public GameplayTagQueryExpression AllTagsExactMatch() { ExprType = EGameplayTagQueryExprType.AllTagsExactMatch; return this; }
    public GameplayTagQueryExpression AnyExprMatch() { ExprType = EGameplayTagQueryExprType.AnyExprMatch; return this; }
    public GameplayTagQueryExpression AllExprMatch() { ExprType = EGameplayTagQueryExprType.AllExprMatch; return this; }
    public GameplayTagQueryExpression NoExprMatch() { ExprType = EGameplayTagQueryExprType.NoExprMatch; return this; }

    public GameplayTagQueryExpression AddTag(GameplayTag Tag)
    {
        TagSet.Add(Tag);
        return this;
    }

    public GameplayTagQueryExpression AddTags(GameplayTagContainer Container)
    {
        if (Container != null)
        {
            TagSet.AddRange(Container.GameplayTags);
        }
        return this;
    }

    public GameplayTagQueryExpression AddExpr(GameplayTagQueryExpression Expr)
    {
        if (Expr != null)
        {
            ExprSet.Add(Expr);
        }
        return this;
    }
    public void EmitTokens(List<byte> TokenStream, List<GameplayTag> Dictionary)
    {
        TokenStream.Add((byte)ExprType);

        if (UsesTagSet())
        {
            TokenStream.Add((byte)TagSet.Count);
            foreach (var Tag in TagSet)
            {
                int Index = Dictionary.IndexOf(Tag);
                if (Index == -1)
                {
                    Index = Dictionary.Count;
                    Dictionary.Add(Tag);
                }
                TokenStream.Add((byte)Index);
            }
        }
        else if (UsesExprSet())
        {
            TokenStream.Add((byte)ExprSet.Count);
            foreach (var Expr in ExprSet)
            {
                Expr.EmitTokens(TokenStream, Dictionary);
            }
        }
    }
    
    private bool UsesTagSet()
    {
        return ExprType == EGameplayTagQueryExprType.AnyTagsMatch ||
               ExprType == EGameplayTagQueryExprType.AllTagsMatch ||
               ExprType == EGameplayTagQueryExprType.NoTagsMatch ||
               ExprType == EGameplayTagQueryExprType.AnyTagsExactMatch ||
               ExprType == EGameplayTagQueryExprType.AllTagsExactMatch;
    }

    private bool UsesExprSet()
    {
        return ExprType == EGameplayTagQueryExprType.AnyExprMatch ||
               ExprType == EGameplayTagQueryExprType.AllExprMatch ||
               ExprType == EGameplayTagQueryExprType.NoExprMatch;
    }
}

public class GameplayTagContainer
{
    public List<GameplayTag> GameplayTags = new List<GameplayTag>();
    public List<GameplayTag> ParentTags = new List<GameplayTag>();

    public GameplayTagContainer()
	{
	}

    public GameplayTagContainer(GameplayTag Tag)
	{
        AddTag(Tag);
	}

    public void AddTag(GameplayTag TagToAdd)
    {
        if (TagToAdd.IsValid() && !GameplayTags.Contains(TagToAdd))
        {
            GameplayTags.Add(TagToAdd);
            
            // 将该标签的所有父标签加入 ParentTags 列表，用于快速匹配
            var Container = GameplayTagsManager.Get().GetSingleTagContainer(TagToAdd);
            foreach (var ParentTag in Container.ParentTags)
            {
                if (!ParentTags.Contains(ParentTag))
                {
                    ParentTags.Add(ParentTag);
                }
            }
        }
    }

    /// <summary>
    /// 检查容器是否包含匹配给定标签的标签（考虑层级）
    /// </summary>
    public bool HasTag(GameplayTag TagToCheck)
    {
        if (!TagToCheck.IsValid()) return false;
        return GameplayTags.Contains(TagToCheck) || ParentTags.Contains(TagToCheck);
    }

    /// <summary>
    /// 检查容器是否包含精确匹配的标签
    /// </summary>
    public bool HasTagExact(GameplayTag TagToCheck)
    {
        if (!TagToCheck.IsValid()) return false;
        return GameplayTags.Contains(TagToCheck);
    }

    /// <summary>
    /// 检查此容器是否包含另一个容器中的【任意】一个标签
    /// </summary>
    public bool HasAny(GameplayTagContainer ContainerToCheck)
    {
		if (ContainerToCheck.IsEmpty())
		{
			return true;
		}
        foreach (var OtherTag in ContainerToCheck.GameplayTags)
        {
            if (GameplayTags.Contains(OtherTag) || ParentTags.Contains(OtherTag))
			{
				return true;
			}
        }
        return false;
    }

    /// <summary>
    /// 检查此容器是否包含另一个容器中的【任意】一个标签（精确匹配）
    /// </summary>
    public bool HasAnyExact(GameplayTagContainer ContainerToCheck)
    {
		if (ContainerToCheck.IsEmpty())
		{
			return true;
		}
        foreach (var OtherTag in ContainerToCheck.GameplayTags)
        {
            if (GameplayTags.Contains(OtherTag))
			{
				return true;
			}
        }
        return false;
    }

    /// <summary>
    /// 检查此容器是否包含另一个容器中的【所有】标签
    /// </summary>
    public bool HasAll(GameplayTagContainer ContainerToCheck)
    {
		if (ContainerToCheck.IsEmpty())
		{
			return true;
		}
        foreach (var OtherTag in ContainerToCheck.GameplayTags)
        {
			if (!GameplayTags.Contains(OtherTag) && !ParentTags.Contains(OtherTag))
			{
				return false;
			}
        }
        return true;
    }

    /// <summary>
    /// 检查此容器是否包含另一个容器中的【所有】标签（精确匹配）
    /// </summary>
    public bool HasAllExact(GameplayTagContainer ContainerToCheck)
    {
		if (ContainerToCheck.IsEmpty())
		{
			return true;
		}
        foreach (var OtherTag in ContainerToCheck.GameplayTags)
        {
            if (!GameplayTags.Contains(OtherTag))
			{
				return false;
			}
        }
        return true;
    }

    public void AppendTags(GameplayTagContainer Other)
    {
        if (Other == null || Other.IsEmpty())
        {
            return;
        }

        int OldTagNum = GameplayTags.Count;
        if (GameplayTags.Capacity < OldTagNum + Other.GameplayTags.Count)
        {
            GameplayTags.Capacity = OldTagNum + Other.GameplayTags.Count;
        }

        foreach (var OtherTag in Other.GameplayTags)
        {
            int SearchIndex = 0;
            while (true)
            {
                if (SearchIndex >= OldTagNum)
                {
                    GameplayTags.Add(OtherTag);
                    break;
                }
                else if (GameplayTags[SearchIndex] == OtherTag)
                {
                    break;
                }
                SearchIndex++;
            }
        }

        int OldParentNum = ParentTags.Count;
        if (ParentTags.Capacity < OldParentNum + Other.ParentTags.Count)
        {
            ParentTags.Capacity = OldParentNum + Other.ParentTags.Count;
        }

        foreach (var OtherTag in Other.ParentTags)
        {
            int SearchIndex = 0;
            while (true)
            {
                if (SearchIndex >= OldParentNum)
                {
                    ParentTags.Add(OtherTag);
                    break;
                }
                else if (ParentTags[SearchIndex] == OtherTag)
                {
                    break;
                }
                SearchIndex++;
            }
        }
    }

    public void FillParentTags()
    {
        ParentTags.Clear();

        if (GameplayTags.Count > 0)
        {
            var TagManager = GameplayTagsManager.Get();
            foreach (var Tag in GameplayTags)
            {
                // 获取该标签的所有父标签并加入容器
                var SingleContainer = TagManager.GetSingleTagContainer(Tag);
                if (SingleContainer != null)
                {
                    foreach (var ParentTag in SingleContainer.ParentTags)
                    {
                        if (!ParentTags.Contains(ParentTag))
                        {
                            ParentTags.Add(ParentTag);
                        }
                    }
                }
            }
        }
    }

    public bool RemoveTag(GameplayTag TagToRemove, bool bDeferParentTags = false)
    {
        bool bRemoved = GameplayTags.Remove(TagToRemove);

        if (bRemoved)
        {
            if (!bDeferParentTags)
            {
                // 必须从头重新计算父标签表，因为可能有多个子标签指向同一个父标签
                FillParentTags();
            }
            return true;
        }
        return false;
    }

    public bool IsEmpty()
    {
        return GameplayTags.Count == 0;
    }

    public bool IsValid()
    {
        return GameplayTags.Count > 0;
    }

    public void Reset()
    {
        GameplayTags.Clear();
        ParentTags.Clear();
    }

    internal int Num()
    {
        return GameplayTags.Count;
    }
}
