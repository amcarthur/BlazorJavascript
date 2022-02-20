using System;
using Microsoft.JSInterop;
using RealGoodApps.BlazorJavascript.Interop.BuiltIns;
using RealGoodApps.BlazorJavascript.Interop.Factories;
using RealGoodApps.BlazorJavascript.Interop.Interfaces;

namespace RealGoodApps.BlazorJavascript.Interop.Extensions
{
    public static class IJSInProcessRuntimeExtensions
    {
        public static IWindow GetWindow(this IJSInProcessRuntime jsRuntime)
        {
            var objectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_getWindow");
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, objectReference);

            if (jsObject is not IWindow window)
            {
                throw new InvalidCastException("The get window method did not return an IWindow.");
            }

            return window;
        }

        public static IJSObject? GetGlobalObjectByName(
            this IJSInProcessRuntime jsRuntime,
            string identifier)
        {
            var objectReference = jsRuntime.Invoke<IJSObjectReference?>("eval", identifier);
            return JSObjectFactory.FromRuntimeObjectReference(jsRuntime, objectReference);
        }

        public static TPrototype? GetGlobalObjectByName<TPrototype>(
            this IJSInProcessRuntime jsRuntime,
            string identifier)
            where TPrototype : class, IJSObject
        {
            var jsObject = GetGlobalObjectByName(jsRuntime, identifier);
            return jsObject as TPrototype;
        }

        public static JSString? CreateString(
            this IJSInProcessRuntime jsRuntime,
            string? stringValue)
        {
            var stringObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructString", stringValue);
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, stringObjectReference);
            return jsObject as JSString;
        }

        public static JSNumber CreatePositiveInfinity(this IJSInProcessRuntime jsRuntime)
        {
            var infinityObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructPositiveInfinity");
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, infinityObjectReference);

            if (jsObject is not JSNumber jsNumber)
            {
                throw new InvalidCastException("The positive infinity constructor did not return a JSNumber.");
            }

            return jsNumber;
        }

        public static JSNumber CreateNegativeInfinity(this IJSInProcessRuntime jsRuntime)
        {
            var infinityObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructNegativeInfinity");
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, infinityObjectReference);

            if (jsObject is not JSNumber jsNumber)
            {
                throw new InvalidCastException("The negative infinity constructor did not return a JSNumber.");
            }

            return jsNumber;
        }

        public static JSNumber CreateNaN(this IJSInProcessRuntime jsRuntime)
        {
            var infinityObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructNaN");
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, infinityObjectReference);

            if (jsObject is not JSNumber jsNumber)
            {
                throw new InvalidCastException("The NaN constructor did not return a JSNumber.");
            }

            return jsNumber;
        }

        public static JSNumber CreateNumberFromDouble(
            this IJSInProcessRuntime jsRuntime,
            double value)
        {
            var numberObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructNumberFromDouble", value);
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, numberObjectReference);

            if (jsObject is not JSNumber jsNumber)
            {
                throw new InvalidCastException("The number from double constructor did not return a JSNumber.");
            }

            return jsNumber;
        }

        public static JSNumber CreateNumberFromInt(
            this IJSInProcessRuntime jsRuntime,
            int value)
        {
            var numberObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructNumberFromInt", value);
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, numberObjectReference);

            if (jsObject is not JSNumber jsNumber)
            {
                throw new InvalidCastException("The number from int constructor did not return a JSNumber.");
            }

            return jsNumber;
        }

        public static JSNumber CreateNumberFromFloat(
            this IJSInProcessRuntime jsRuntime,
            float value)
        {
            var numberObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructNumberFromFloat", value);
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, numberObjectReference);

            if (jsObject is not JSNumber jsNumber)
            {
                throw new InvalidCastException("The number from float constructor did not return a JSNumber.");
            }

            return jsNumber;
        }

        public static JSBoolean CreateBoolean(
            this IJSInProcessRuntime jsRuntime,
            bool value)
        {
            var numberObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructBoolean", value);
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, numberObjectReference);

            if (jsObject is not JSBoolean jsBoolean)
            {
                throw new InvalidCastException("The boolean constructor did not return a JSBoolean.");
            }

            return jsBoolean;
        }

        public static JSArray CreateArray(
            this IJSInProcessRuntime jsRuntime)
        {
            var arrayObjectReference = jsRuntime.Invoke<IJSObjectReference?>("__blazorJavascript_constructArray");
            var jsObject = JSObjectFactory.FromRuntimeObjectReference(jsRuntime, arrayObjectReference);

            if (jsObject is not JSArray jsArray)
            {
                throw new InvalidCastException("The array constructor did not return a JSArray.");
            }

            return jsArray;
        }
    }
}
