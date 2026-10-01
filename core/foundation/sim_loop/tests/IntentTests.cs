using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="Intent"/>（意图原语）的直接用例（T-M1，2026-10-01 测试覆盖第四批）：Kind 格式、
    /// <c>ToJson/FromJson</c> 往返、args 缺省、相等性。期望值由规则（Kind 正则
    /// <c>^[a-z][a-z0-9_]*$</c>、往返恒等）推出，不写死裸数。
    /// </summary>
    public sealed class IntentTests
    {
        private static readonly Id Actor = new Id("unit.hero");

        [Theory]
        [InlineData("move")]
        [InlineData("cast_skill")]
        [InlineData("a")]
        [InlineData("a1_b2")]
        [InlineData("x_")]
        public void Kind_MatchingLowerSnakeFormat_IsAccepted(string kind)
        {
            var intent = new Intent(Actor, kind);
            Assert.Equal(kind, intent.Kind);
            Assert.Equal(Actor, intent.ActorId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Move")]
        [InlineData("mOve")]
        [InlineData("1move")]
        [InlineData("_move")]
        [InlineData("move-fast")]
        [InlineData("move fast")]
        [InlineData(" move")]
        [InlineData("move ")]
        [InlineData("mové")]
        public void Kind_NotMatchingFormat_ThrowsArgumentException(string kind)
        {
            var ex = Assert.Throws<ArgumentException>(() => new Intent(Actor, kind));
            Assert.Equal("kind", ex.ParamName);
        }

        [Fact]
        public void Kind_Null_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Intent(Actor, null!));
        }

        /// <summary>复现：.NET 正则的 <c>$</c> 默认也匹配"字符串末尾换行符之前"，原实现的
        /// <c>^[a-z][a-z0-9_]*$</c> 会放过 <c>"move\n"</c>，违反"全小写、下划线分隔"的格式契约
        /// （序列化进回放/存档后 Kind 带控制字符）。修复后以 <c>\z</c> 收尾。</summary>
        [Theory]
        [InlineData("move\n")]
        [InlineData("move\r\n")]
        [InlineData("a\n")]
        public void Kind_WithTrailingNewline_IsRejected(string kind)
        {
            Assert.Throws<ArgumentException>(() => new Intent(Actor, kind));
        }

        [Fact]
        public void Args_OmittedOrNull_IsEmptyObject_NotNull()
        {
            var omitted = new Intent(Actor, "move");
            var explicitNull = new Intent(Actor, "move", null);

            Assert.NotNull(omitted.Args);
            Assert.Empty(omitted.Args);
            Assert.NotNull(explicitNull.Args);
            Assert.Empty(explicitNull.Args);
        }

        [Fact]
        public void Args_Provided_IsCarriedByReference()
        {
            var args = new JsonObjectBuilder().Add("x", new JsonNumber(3)).Build();
            var intent = new Intent(Actor, "move", args);
            Assert.Same(args, intent.Args);
        }

        [Fact]
        public void ToJson_ThenFromJson_RoundTripsActorKindAndArgs()
        {
            var args = new JsonObjectBuilder()
                .Add("target", new JsonString("unit.foe_1"))
                .Add("range", new JsonNumber(2.5))
                .Build();
            var original = new Intent(Actor, "cast_skill", args);

            var json = original.ToJson();
            var restored = Intent.FromJson(json);

            Assert.Equal(original.ActorId, restored.ActorId);
            Assert.Equal(original.Kind, restored.Kind);
            Assert.Equal(JsonWriter.Write(original.Args), JsonWriter.Write(restored.Args));
        }

        [Fact]
        public void ToJson_HasExactlyActorIdKindArgsKeys_InThatOrder()
        {
            var json = new Intent(Actor, "move").ToJson();

            Assert.Equal(new[] { "actorId", "kind", "args" }, new List<string>(json.Keys));
            Assert.Equal(Actor.Value, ((JsonString)json["actorId"]).Value);
            Assert.Equal("move", ((JsonString)json["kind"]).Value);
            Assert.IsType<JsonObject>(json["args"]);
        }

        [Fact]
        public void FromJson_ThroughTextRoundTrip_PreservesIntent()
        {
            var original = new Intent(Actor, "move", new JsonObjectBuilder().Add("dx", new JsonNumber(-1)).Build());

            var text = JsonWriter.Write(original.ToJson());
            var restored = Intent.FromJson((JsonObject)JsonReader.Parse(text));

            Assert.Equal(JsonWriter.Write(original.ToJson()), JsonWriter.Write(restored.ToJson()));
        }

        [Fact]
        public void FromJson_ArgsMissing_YieldsEmptyArgs()
        {
            var json = new JsonObjectBuilder()
                .Add("actorId", new JsonString(Actor.Value))
                .Add("kind", new JsonString("move"))
                .Build();

            var intent = Intent.FromJson(json);

            Assert.Equal("move", intent.Kind);
            Assert.NotNull(intent.Args);
            Assert.Empty(intent.Args);
        }

        [Fact]
        public void FromJson_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Intent.FromJson(null!));
        }

        [Fact]
        public void FromJson_MissingActorIdOrKind_Throws()
        {
            var noActor = new JsonObjectBuilder().Add("kind", new JsonString("move")).Build();
            var noKind = new JsonObjectBuilder().Add("actorId", new JsonString(Actor.Value)).Build();

            Assert.ThrowsAny<Exception>(() => Intent.FromJson(noActor));
            Assert.ThrowsAny<Exception>(() => Intent.FromJson(noKind));
        }

        [Fact]
        public void FromJson_IllegalKindInPayload_ThrowsArgumentException()
        {
            var json = new JsonObjectBuilder()
                .Add("actorId", new JsonString(Actor.Value))
                .Add("kind", new JsonString("Move"))
                .Build();

            Assert.Throws<ArgumentException>(() => Intent.FromJson(json));
        }

        [Fact]
        public void Equality_SameActorKindAndSameArgsInstance_IsEqual_WithMatchingHashCode()
        {
            var args = new JsonObjectBuilder().Add("n", new JsonNumber(1)).Build();
            var a = new Intent(Actor, "move", args);
            var b = new Intent(Actor, "move", args);

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equality_DefaultArgsIntents_AreEqual()
        {
            Assert.Equal(new Intent(Actor, "move"), new Intent(Actor, "move"));
        }

        [Fact]
        public void Equality_DifferentActorOrKind_IsNotEqual()
        {
            var baseline = new Intent(Actor, "move");

            Assert.NotEqual(baseline, new Intent(new Id("unit.other"), "move"));
            Assert.NotEqual(baseline, new Intent(Actor, "cast"));
        }

        [Fact]
        public void Equality_SameContentButDistinctArgsInstances_IsNotEqual_ByReferenceSemantics()
        {
            // Equals 对 Args 用引用相等（契约：搬运不解释）；内容相同的两个独立 JsonObject 不等。
            var a = new Intent(Actor, "move", new JsonObjectBuilder().Add("n", new JsonNumber(1)).Build());
            var b = new Intent(Actor, "move", new JsonObjectBuilder().Add("n", new JsonNumber(1)).Build());

            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_Boxed_NonIntent_IsFalse()
        {
            Assert.False(new Intent(Actor, "move").Equals("move"));
            Assert.False(new Intent(Actor, "move").Equals(null));
        }

        [Fact]
        public void ToString_ContainsActorAndKind()
        {
            Assert.Equal("Intent(actorId=unit.hero, kind=move)", new Intent(Actor, "move").ToString());
        }
    }
}
