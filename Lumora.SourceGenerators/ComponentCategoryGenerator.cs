using System;
using System.Drawing;
using System.Net.NetworkInformation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
namespace Lumora.SourceGenerators
{

    [Generator(LanguageNames.CSharp)]
    public class ComponentListGenerator : IIncrementalGenerator
    {
        private static readonly SymbolDisplayFormat FullyQulifyedNameWithoutGlobal = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted
        );
        private static ExpressionSyntax Id(string name) => IdentifierName(name);
        private static ExpressionSyntax Lte(string literal) => LiteralExpression(SyntaxKind.StringLiteralExpression,Literal(literal));
        private static AssignmentExpressionSyntax Assign(string left, ExpressionSyntax right) => AssignmentExpression(
        SyntaxKind.SimpleAssignmentExpression,
        Id(left),
        right);
        private static AnonymousObjectMemberDeclaratorSyntax AnonymousDeclaratorNameExpression(string name,ExpressionSyntax value) => AnonymousObjectMemberDeclarator(value).WithNameEquals(NameEquals(IdentifierName(name)));
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var provider = context.SyntaxProvider.ForAttributeWithMetadataName("Lumora.Core.ComponentCategoryAttribute", static (_, _) => true, static (sybmol, _) =>
            {
                return sybmol;
            });
            var compiltationtarget = context.CompilationProvider.Combine(provider.Collect());
            context.RegisterSourceOutput(compiltationtarget, static (spc, source) =>
            {
                INamedTypeSymbol? componenttypeinfo = source.Left.GetTypeByMetadataName("Lumora.Core.GodotUI.Inspectors.ComponentTypeInfo");
                if(componenttypeinfo is null)
                {
                    return;
                }
                List<SyntaxNodeOrToken> cases = new((source.Right.Length * 2) + 2);
                List<SyntaxNodeOrToken> typeslist = new(source.Right.Length);
                var metadatatypename = ParseTypeName("Lumora.Core.GodotUI.Inspectors.ComponentTypeInfo");
                foreach (var component in source.Right)
                {
                    foreach (var data in component.Attributes)
                    {
                        if (data.ConstructorArguments.Length > 0 && SymbolEqualityComparer.Default.Equals(data.ConstructorArguments[0].Type, source.Left.GetSpecialType(SpecialType.System_String)))
                        {
                            var parsedtypename = ParseTypeName(component.TargetSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                            var typeofname = TypeOfExpression(parsedtypename);
                            var shorttypename = component.TargetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                            var category = data.ConstructorArguments[0].Value as string;
                            if(category is null)return;
                            cases.Add(SwitchExpressionArm(
                                ConstantPattern(
                                    Lte($"{category}/{shorttypename}")
                                ),
                                typeofname
                            ));
                            cases.Add(Token(SyntaxKind.CommaToken));
                            var typefullname = component.TargetSymbol.ToDisplayString(FullyQulifyedNameWithoutGlobal);
                            string namespacename = component.TargetSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                            typeslist.Add(
                                    ObjectCreationExpression(metadatatypename).WithInitializer(
                                    InitializerExpression(SyntaxKind.ObjectInitializerExpression, 
                                    
                                    [
                                    Assign("Type",typeofname),
                                    Assign("Name",Lte(shorttypename)),
                                    Assign("FullName",Lte(typefullname)),
                                    Assign("Category",Lte(category)),
                                    Assign("Namespace",Lte(namespacename))
                                ]))
                            );
                            typeslist.Add(Token(SyntaxKind.CommaToken));
                        }
                    }
                }
                cases.Add(SwitchExpressionArm(DiscardPattern(),
                                                LiteralExpression(SyntaxKind.NullLiteralExpression)
                                                )
                            );
                var arraytype = ArrayType(ArrayType(metadatatypename).WithRankSpecifiers([
                                        ArrayRankSpecifier([OmittedArraySizeExpression()])
                                    ]));
                var unit = CompilationUnit()
    .WithMembers(
        SingletonList<MemberDeclarationSyntax>(
            FileScopedNamespaceDeclaration(
                QualifiedName(
                    IdentifierName("Lumora"),
                    IdentifierName("Core")))
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    ClassDeclaration("ComponentLibrary")
                    .WithModifiers(
                        TokenList(
                            new[]{
                            Token(SyntaxKind.PublicKeyword),
                            Token(SyntaxKind.StaticKeyword)}))
                    .WithMembers(
                        new SyntaxList<MemberDeclarationSyntax>(
                            [
                            MethodDeclaration(
                                QualifiedName(
                                    IdentifierName("System"),
                                    IdentifierName("Type")),
                                Identifier("GetFromCategoryOrNull"))
                            .WithModifiers(
                                TokenList(
                                    new[]{
                                    Token(SyntaxKind.PublicKeyword),
                                    Token(SyntaxKind.StaticKeyword)}))
                            .WithParameterList(
                                ParameterList(
                                    SingletonSeparatedList<ParameterSyntax>(
                                        Parameter(
                                            Identifier("name"))
                                        .WithType(
                                            QualifiedName(
                                                IdentifierName("System"),
                                                IdentifierName("String"))))))
                            .WithBody(
                                Block(
                                    SingletonList<StatementSyntax>(
                                        ReturnStatement(
                                            SwitchExpression(
                                                IdentifierName("name"))
                                            .WithArms(
                                                SeparatedList<SwitchExpressionArmSyntax>(
                                                    cases
                                                ))))))                            
                            ,
                            FieldDeclaration
                                (
                                    default,
                                    [Token(SyntaxKind.PublicKeyword),Token(SyntaxKind.StaticKeyword),Token(SyntaxKind.ReadOnlyKeyword)],
                                    VariableDeclaration(
                                    arraytype
                                ).WithVariables([
                                    VariableDeclarator(Identifier("ComponentList"))
                                        .WithInitializer(EqualsValueClause(ArrayCreationExpression(arraytype).WithInitializer(InitializerExpression(SyntaxKind.ArrayInitializerExpression,SeparatedList<ExpressionSyntax>(typeslist)))))
                                ]))
                            ]))
                                                ))))
    .NormalizeWhitespace();
                spc.AddSource("ComponentTypes.g.cs", unit.ToFullString());
            });
        }
    }
}