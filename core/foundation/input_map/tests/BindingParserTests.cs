using System;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    public class BindingParserTests
    {
        [Fact]
        public void Parse_Key_ReturnsKeyKindAndName()
        {
            var b = BindingParser.Parse("key:w");
            Assert.Equal(BindingKind.Key, b.Kind);
            Assert.Equal("w", b.Name);
            Assert.True(b.IsDigital);
        }

        [Fact]
        public void Parse_Mouse_ReturnsMouseKindAndName()
        {
            var b = BindingParser.Parse("mouse:left");
            Assert.Equal(BindingKind.Mouse, b.Kind);
            Assert.Equal("left", b.Name);
        }

        [Fact]
        public void Parse_PadButton_ReturnsPadButtonKindAndName()
        {
            var b = BindingParser.Parse("pad:a");
            Assert.Equal(BindingKind.PadButton, b.Kind);
            Assert.Equal("a", b.Name);
        }

        [Fact]
        public void Parse_PadAxis_ReturnsPadAxisKindAndName()
        {
            var b = BindingParser.Parse("pad_axis:zoom");
            Assert.Equal(BindingKind.PadAxis, b.Kind);
            Assert.Equal("zoom", b.Name);
            Assert.False(b.IsDigital);
        }

        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        public void Parse_PadStick_LeftOrRight_ReturnsPadStickKind(string side)
        {
            var b = BindingParser.Parse("pad_stick:" + side);
            Assert.Equal(BindingKind.PadStick, b.Kind);
            Assert.Equal(side, b.Name);
        }

        [Fact]
        public void Parse_PadStick_InvalidValue_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("pad_stick:up"));
        }

        [Fact]
        public void Parse_Composite2D_Valid_ReturnsFourDigitalSubBindings()
        {
            var b = BindingParser.Parse("composite2d:key:w|key:s|key:a|key:d");
            Assert.Equal(BindingKind.Composite2D, b.Kind);
            Assert.Null(b.Name);
            Assert.Equal("w", b.Up!.Name);
            Assert.Equal("s", b.Down!.Name);
            Assert.Equal("a", b.Left!.Name);
            Assert.Equal("d", b.Right!.Name);
            Assert.True(b.Up.IsDigital);
        }

        [Fact]
        public void Parse_Composite2D_MixedDeviceSubBindings_Allowed()
        {
            var b = BindingParser.Parse("composite2d:pad:dpad_up|pad:dpad_down|key:a|mouse:left");
            Assert.Equal(BindingKind.PadButton, b.Up!.Kind);
            Assert.Equal(BindingKind.PadButton, b.Down!.Kind);
            Assert.Equal(BindingKind.Key, b.Left!.Kind);
            Assert.Equal(BindingKind.Mouse, b.Right!.Kind);
        }

        [Fact]
        public void Parse_Composite2D_WrongPartCount_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("composite2d:key:w|key:s|key:a"));
        }

        [Fact]
        public void Parse_Composite2D_NestedAxisSubBinding_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("composite2d:pad_axis:x|key:s|key:a|key:d"));
        }

        [Fact]
        public void Parse_Composite2D_EmptySubBinding_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("composite2d:|key:s|key:a|key:d"));
        }

        [Fact]
        public void Parse_UnknownPrefix_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("touch:tap"));
        }

        [Fact]
        public void Parse_MissingColon_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("keyw"));
        }

        [Fact]
        public void Parse_EmptyString_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse(""));
        }

        [Fact]
        public void Parse_EmptyNameAfterColon_Throws()
        {
            Assert.Throws<ArgumentException>(() => BindingParser.Parse("key:"));
        }
    }
}
