using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SqlToLinq.Core;

namespace SqlToLinq.Tests {

    /// <summary>
    /// Property-based / fuzz teszt: sok véletlen, de a nyelvtannak megfelelő SQL
    /// lekérdezést generál, és minden egyesre csak azt ellenőrzi, hogy (a) a
    /// transpiler nem dob kivételt, és (b) a kimenet szintaktikailag érvényes C#
    /// kifejezés (Roslyn parse-olja, hiba nélkül). Ez nem helyettesíti a
    /// SemanticEquivalenceTests-et, de olyan eseteket is felfedhet, amikre
    /// sosem gondoltunk volna kézzel megírt JSON teszteset formájában.
    /// </summary>
    [TestFixture]
    public class FuzzTests {

        [Test]
        public void Randomly_Generated_Sql_Should_Not_Throw_And_Should_Produce_Valid_Csharp() {

            var generator = new RandomSqlGenerator(seed: 12345);

            for (int i = 0; i < 500; i++) {

                string sql = generator.NextSelect();
                string linq;

                try {
                    linq = SqlToLinqConverter.Convert(sql);
                } catch (System.Exception ex) {
                    Assert.Fail($"[ERROR] A transpiler kivételt dobott.\nSQL: {sql}\n{ex}");
                    return;
                }

                var expression = SyntaxFactory.ParseExpression(linq);
                var errors = expression.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                Assert.That(errors, Is.Empty,
                    $"[ERROR] A generált kód nem érvényes C#.\nSQL: {sql}\nLINQ: {linq}\nHibák: {string.Join(", ", errors)}");
            }
        }
    }
}
