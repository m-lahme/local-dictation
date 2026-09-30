using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace LocalDictation
{
    public sealed class SelectedTextResult
    {
        public string Text { get; init; } = "";
        public uint ProcessId { get; init; }
        public bool Success => Text.Length > 0;
    }

    public static class SelectionCapture
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        public static uint ForegroundProcessId()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
            return processId;
        }

        public static async Task<SelectedTextResult> ReadFocusedSelectionAsync()
        {
            uint processId = ForegroundProcessId();
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element != null && element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
                {
                    var ranges = ((TextPattern)pattern).GetSelection();
                    if (ranges != null && ranges.Length > 0)
                    {
                        string text = String.Concat(Array.ConvertAll(ranges, x => x.GetText(-1))).TrimEnd('\r', '\n');
                        if (!String.IsNullOrWhiteSpace(text)) return new SelectedTextResult { Text = text, ProcessId = processId };
                    }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            return await ReadThroughClipboard(processId);
        }

        static async Task<SelectedTextResult> ReadThroughClipboard(uint processId)
        {
            using var transfer=new ClipboardTransfer();
            if(!transfer.TrySnapshot(out var snapshot))return new SelectedTextResult{ProcessId=processId};
            HashSet<string> historyIds=null;bool historyEnabled=false;
            try
            {
                var before=await Clipboard.GetHistoryItemsAsync();
                if(before.Status==ClipboardHistoryItemsResultStatus.Success){historyEnabled=true;historyIds=before.Items.Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);}
                else if(before.Status!=ClipboardHistoryItemsResultStatus.ClipboardHistoryDisabled)return new SelectedTextResult{ProcessId=processId};
            }
            catch{return new SelectedTextResult{ProcessId=processId};}
            for(int i=0;i<20&&!ClipboardInserter.ModifiersReleased();i++)await Task.Delay(25);
            if(!ClipboardInserter.CopySelection())return new SelectedTextResult{ProcessId=processId};
            uint sequence=snapshot.Sequence;for(int i=0;i<20&&ClipboardTransfer.CurrentSequence==sequence;i++)await Task.Delay(25);
            uint copiedSequence=ClipboardTransfer.CurrentSequence;if(copiedSequence==sequence)return new SelectedTextResult{ProcessId=processId};
            string text="";for(int i=0;i<8;i++){try{if(System.Windows.Clipboard.ContainsText()){text=System.Windows.Clipboard.GetText();break;}}catch(COMException){}await Task.Delay(25);}
            if(historyEnabled)try{var after=await Clipboard.GetHistoryItemsAsync();if(after.Status==ClipboardHistoryItemsResultStatus.Success)foreach(var item in after.Items.Where(x=>!historyIds.Contains(x.Id)))Clipboard.DeleteItemFromHistory(item);}catch{}
            if(ClipboardTransfer.CurrentSequence==copiedSequence)try{transfer.SetExcluded(snapshot.Formats);}catch{return new SelectedTextResult{ProcessId=processId};}
            return String.IsNullOrWhiteSpace(text)?new SelectedTextResult{ProcessId=processId}:new SelectedTextResult{Text=text.TrimEnd('\r','\n'),ProcessId=processId};
        }
    }
}
