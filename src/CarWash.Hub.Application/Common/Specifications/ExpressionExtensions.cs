using System.Linq.Expressions;

namespace CarWash.Hub.Application.Common.Specifications;

/// <summary>
/// Combines predicate expressions so specifications can accumulate filters.
/// </summary>
/// <remarks>Layer: Application.</remarks>
public static class ExpressionExtensions
{
    /// <summary>
    /// Combines two predicates with a logical AND.
    /// </summary>
    /// <typeparam name="T">The predicate's input type.</typeparam>
    /// <param name="left">The left predicate.</param>
    /// <param name="right">The right predicate.</param>
    /// <returns>A predicate matching both.</returns>
    /// <remarks>
    /// The two lambdas have different parameter instances, so their bodies cannot simply be
    /// joined - EF Core would see an unbound parameter and fail to translate the tree. The
    /// rewriter below rebinds the right-hand body onto the left-hand parameter first.
    /// </remarks>
    public static Expression<Func<T, bool>> AndAlso<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var parameter = Expression.Parameter(typeof(T), "e");

        var leftBody = new ParameterRebinder(left.Parameters[0], parameter).Visit(left.Body)!;
        var rightBody = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body)!;

        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(leftBody, rightBody), parameter);
    }

    /// <summary>
    /// Combines two predicates with a logical OR.
    /// </summary>
    /// <typeparam name="T">The predicate's input type.</typeparam>
    /// <param name="left">The left predicate.</param>
    /// <param name="right">The right predicate.</param>
    /// <returns>A predicate matching either.</returns>
    public static Expression<Func<T, bool>> OrElse<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var parameter = Expression.Parameter(typeof(T), "e");

        var leftBody = new ParameterRebinder(left.Parameters[0], parameter).Visit(left.Body)!;
        var rightBody = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body)!;

        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(leftBody, rightBody), parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
