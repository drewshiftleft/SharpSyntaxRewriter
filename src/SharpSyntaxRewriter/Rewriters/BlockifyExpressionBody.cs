﻿// Copyright 2021 ShiftLeft, Inc.
// Author: Leandro T. C. Melo

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;

using SharpSyntaxRewriter.Adapters;
using SharpSyntaxRewriter.Rewriters.Types;
using SharpSyntaxRewriter.Utilities;

namespace SharpSyntaxRewriter.Rewriters
{
    public class BlockifyExpressionBody : SymbolicRewriter
    {
        public override string Name()
        {
            return "<blockify expression body>";
        }

        private static BlockSyntax WrapInBlock(ExpressionSyntax exprNode,
                                               bool withValuedRet)
        {
            Debug.Assert(exprNode != null);

            var blockNode =
                SyntaxFactory.Block(
                    exprNode is ThrowExpressionSyntax throwExpr
                        ? SyntaxFactory.ThrowStatement(throwExpr.Expression)
                        : withValuedRet
                            ? SyntaxFactory.ReturnStatement(exprNode.WithoutTrivia())
                            : SyntaxFactory.ExpressionStatement(exprNode.WithoutTrivia()));

            return blockNode;
        }

        private static BlockSyntax VisitThroughAdapter(IFunctionSyntaxAdapter funcAdapter,
                                                       TypeSyntax retTySpec)
        {
            Debug.Assert(funcAdapter.ExpressionBody != null);

            if (retTySpec != null
                    && ReturnTypeInfo.ImpliesVoid(
                            retTySpec,
                            ModifiersChecker.Has_async(funcAdapter.Modifiers)))
            {
                retTySpec = null;
            }

            return WrapInBlock(funcAdapter.ExpressionBody, retTySpec != null);
        }

        private static SyntaxNode VisitBaseMethodDeclaration<MethodDeclarationT>(
                MethodDeclarationT node,
                Func<MethodDeclarationT, SyntaxNode> visit,
                TypeSyntax retTySpec)
            where MethodDeclarationT : BaseMethodDeclarationSyntax
        {
            var node_P = (MethodDeclarationT)visit(node);

            if (node_P.ExpressionBody == null)
                return node_P;

            var blockNode =
                VisitThroughAdapter(new AdaptedBaseMethodDeclaration(node_P), retTySpec);

            var blockNode_P = blockNode
                .WithOpenBraceToken(blockNode.OpenBraceToken
                    .WithLeadingTrivia(
                        node_P.ExpressionBody.ArrowToken.LeadingTrivia.AddRange(
                            node_P.ExpressionBody.ArrowToken.TrailingTrivia.AddRange(
                                node_P.ExpressionBody.GetLeadingTrivia()))))
                .WithCloseBraceToken(blockNode.CloseBraceToken
                    .WithTrailingTrivia(
                        node_P.ExpressionBody.GetTrailingTrivia().AddRange(
                            node_P.SemicolonToken.LeadingTrivia.AddRange(
                                node_P.SemicolonToken.TrailingTrivia))));

            node_P = (MethodDeclarationT)node_P
                .RemoveNode(node_P.ExpressionBody, SyntaxRemoveOptions.KeepNoTrivia)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                .WithBody(blockNode_P);

            return node_P;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            return VisitBaseMethodDeclaration(node,
                                              base.VisitMethodDeclaration,
                                              node.ReturnType);
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            return VisitBaseMethodDeclaration(node,
                                              base.VisitConstructorDeclaration,
                                              null);
        }

        public override SyntaxNode VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            return VisitBaseMethodDeclaration(node,
                                              base.VisitDestructorDeclaration,
                                              null);
        }

        public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            return VisitBaseMethodDeclaration(node,
                                              base.VisitOperatorDeclaration,
                                              node.ReturnType);
        }

        public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            return VisitBaseMethodDeclaration(node,
                                              base.VisitConversionOperatorDeclaration,
                                              node.Type);
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var node_P = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node);

            if (node_P.ExpressionBody == null)
                return node_P;

            var blockNode = VisitThroughAdapter(new AdaptedLocalFunction(node_P), node.ReturnType);

            var blockNode_P = blockNode
                .WithOpenBraceToken(blockNode.OpenBraceToken
                    .WithLeadingTrivia(
                        node_P.ExpressionBody.ArrowToken.LeadingTrivia.AddRange(
                            node_P.ExpressionBody.ArrowToken.TrailingTrivia.AddRange(
                                node_P.ExpressionBody.GetLeadingTrivia()))))
                .WithCloseBraceToken(blockNode.CloseBraceToken
                    .WithTrailingTrivia(
                        node_P.ExpressionBody.GetTrailingTrivia().AddRange(
                            node_P.SemicolonToken.LeadingTrivia.AddRange(
                                node_P.SemicolonToken.TrailingTrivia))));

            return node_P
                    .RemoveNode(node_P.ExpressionBody, SyntaxRemoveOptions.KeepNoTrivia)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithBody(blockNode_P);
        }

        private SyntaxNode VisitLambdaExpression<LambdaT>(
                LambdaT node,
                Func<LambdaT, SyntaxNode> visit)
            where LambdaT : LambdaExpressionSyntax
        {
            var node_P = (LambdaT)visit(node);

            if (node_P.ExpressionBody == null || IsExpressionTreeVisit(node))
                return node_P;

            var methSym = _semaModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            var blockNode = WrapInBlock(
                node_P.ExpressionBody,
                !ReturnTypeInfo.ImpliesVoid(methSym.ReturnType, methSym.IsAsync));

            var blockNodeP = blockNode
                .WithOpenBraceToken(blockNode.OpenBraceToken
                    .WithLeadingTrivia(
                        node_P.ExpressionBody.GetLeadingTrivia().AddRange(
                            node_P.ExpressionBody.GetTrailingTrivia())))
                .WithCloseBraceToken(blockNode.CloseBraceToken
                    .WithTrailingTrivia(
                        node_P.ExpressionBody.GetTrailingTrivia()));

            return node_P
                .RemoveNode(node_P.ExpressionBody, SyntaxRemoveOptions.KeepNoTrivia)
                .WithBody(blockNodeP);
        }

        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            if (IsExpressionTreeVisit(node))
                return node;

            return VisitLambdaExpression(node, base.VisitSimpleLambdaExpression);
        }

        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            if (IsExpressionTreeVisit(node))
                return node;

            return VisitLambdaExpression(node, base.VisitParenthesizedLambdaExpression);
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var node_P = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);

            if (node_P.ExpressionBody == null)
                return node_P;

            var blockNode = WrapInBlock(node_P.ExpressionBody.Expression, true);

            var accessDecls =
                SyntaxFactory.AccessorList(
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.AccessorDeclaration(
                            SyntaxKind.GetAccessorDeclaration,
                            SyntaxFactory.List<AttributeListSyntax>(),
                            SyntaxFactory.TokenList(),
                            blockNode)))
                .WithLeadingTrivia(
                    SyntaxFactory.TriviaList()
                        .AddRange(node.ExpressionBody.ArrowToken.TrailingTrivia)
                        .AddRange(node.ExpressionBody.Expression.GetLeadingTrivia()));

            return node_P
                    .RemoveNode(node_P.ExpressionBody, SyntaxRemoveOptions.KeepEndOfLine)
                    .WithAccessorList(accessDecls)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithTriviaFrom(node);
        }

        public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            var node_P = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node);

            if (node_P.ExpressionBody == null)
                return node_P;

            return node_P
                    .RemoveNode(node_P.ExpressionBody, SyntaxRemoveOptions.KeepEndOfLine)
                    .WithAccessorList(
                        SyntaxFactory.AccessorList( 
                            SyntaxFactory.List<AccessorDeclarationSyntax>().Add(
                                SyntaxFactory.AccessorDeclaration(
                                    SyntaxKind.GetAccessorDeclaration,
                                    SyntaxFactory.Block(
                                        SyntaxFactory.ReturnStatement(
                                            node_P.ExpressionBody.Expression)))))
                        .WithLeadingTrivia(
                            SyntaxFactory.TriviaList()
                                .AddRange(node.ExpressionBody.ArrowToken.TrailingTrivia)
                                .AddRange(node.ExpressionBody.Expression.GetLeadingTrivia())))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithTriviaFrom(node);
        }

        public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            var node_P = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node);

            if (node_P.ExpressionBody == null)
                return node_P;

            BlockSyntax blockNode;
            switch (node_P.Kind())
            {
                case SyntaxKind.GetAccessorDeclaration:
                    blockNode = WrapInBlock(node_P.ExpressionBody.Expression, true);
                    break;

                case SyntaxKind.SetAccessorDeclaration:
                case SyntaxKind.InitAccessorDeclaration:
                    blockNode = WrapInBlock(node_P.ExpressionBody.Expression, false);
                    break;

                default:
                    return node_P;
            }

            Debug.Assert(blockNode != null);

            return node_P.RemoveNode(node_P.ExpressionBody, SyntaxRemoveOptions.KeepEndOfLine)
                        .WithBody(blockNode)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                        .WithTriviaFrom(node);
        }

        public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
        {
            return node;
        }

        public override SyntaxNode VisitQueryExpression(QueryExpressionSyntax node)
        {
            return node;
        }
    }
}