using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalDictation;

internal static class ClipboardAudit
{
    static byte[] Utf(string value)=>Encoding.Unicode.GetBytes(value+"\0");
    public static void Run(string data,bool disposable=false)
    {
        var app=new Application();var panel=new StackPanel{Margin=new Thickness(20)};
        var box=new TextBox{AcceptsReturn=true,Height=220,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var label=new TextBlock{Text="Prüft langes Einfügen, Formatwiederherstellung, spätes Abrufen und paralleles Kopieren. Unbekannte bestehende Zwischenablageformate bleiben unangetastet.",TextWrapping=TextWrapping.Wrap};
        var button=new Button{Content="Zwischenablage-Test starten",Margin=new Thickness(0,12,0,12),Padding=new Thickness(12)};panel.Children.Add(label);panel.Children.Add(button);panel.Children.Add(box);
        var window=new Window{Title="Local Dictation Zwischenablage-Test",Width=760,Height=470,Content=panel};
        button.Click+=async(s,e)=>{
            button.IsEnabled=false;var log=new List<string>();
            using var initial=new ClipboardTransfer();
            if(!initial.TrySnapshot(out var original)&&disposable){original=new ClipboardSnapshot();original.Formats.Add(new(13,Utf(Clipboard.ContainsText()?Clipboard.GetText():"")));}
            if(original==null){label.Text="ÜBERSPRUNGEN: Bestehende Formate sind nicht sicher kopierbar. Bitte einen einfachen Text kopieren und erneut starten.";button.IsEnabled=true;return;}
            void Check(bool ok,string name){if(!ok)throw new Exception(name);log.Add("PASS "+name);}
            bool Newer=false;
            byte[] baseline=Utf("Vorherige Zwischenablage: Straße und Größe.");
            var fixture=new List<KeyValuePair<uint,byte[]>>{new(13,baseline),new(ClipboardTransfer.RegisterClipboardFormat("HTML Format"),Encoding.UTF8.GetBytes("<b>Größe</b>\0")),new(ClipboardTransfer.RegisterClipboardFormat("Rich Text Format"),Encoding.ASCII.GetBytes("{\\rtf1 original}\0"))};
            void Prepare(){box.Clear();initial.SetExcluded(fixture);box.Focus();Keyboard.Focus(box);}
            try{
                var historyBefore=await Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync();
                var oleText=new DataObject();oleText.SetText("Vorheriger OLE-Text");oleText.SetData("CanIncludeInClipboardHistory",new MemoryStream(new byte[4]));oleText.SetData("CanUploadToCloudClipboard",new MemoryStream(new byte[4]));oleText.SetData("ExcludeClipboardContentFromMonitorProcessing",new MemoryStream(new byte[4]));Clipboard.SetDataObject(oleText,true);
                Check(initial.TrySnapshot(out var oleSnapshot),"plain OLE text with DataObject transport is safely captured: "+initial.SnapshotReason);
                box.Focus();Keyboard.Focus(box);var oleDelivery=await ClipboardInserter.DeliverAsync("OLE ohne Zeichen-Fallback.",OutputMode.Insert);Check(oleDelivery.Success&&oleDelivery.Method=="paste-and-restore"&&Clipboard.GetText()=="Vorheriger OLE-Text","OLE text uses fast paste and restores original content");
                Prepare();Check(initial.TrySnapshot(out var before),"text, HTML and RTF snapshot");
                string expected=string.Concat(Enumerable.Repeat("Grüße aus Köln. Straße, CAFÉ und 😀. ",900));
                var timer=Stopwatch.StartNew();long textMs=0;TextChangedEventHandler seen=(a,b)=>{if(box.Text.Length==expected.Length)textMs=timer.ElapsedMilliseconds;};box.TextChanged+=seen;
                int starts=0;long startMs=-1;var delivered=await ClipboardInserter.DeliverAsync(expected,OutputMode.Insert,()=>{starts++;startMs=timer.ElapsedMilliseconds;});box.TextChanged-=seen;
                Check(delivered.Success&&delivered.Method=="paste-and-restore","long text uses one paste: "+delivered.Error);Check(box.Text==expected,"all "+expected.Length+" characters and capitalization intact");Check(starts==1&&startMs<=textMs,"success animation starts with input, before restoration");
                log.Add("MEASURE target text complete "+textMs+" ms; restore complete "+timer.ElapsedMilliseconds+" ms");
                Check(initial.TrySnapshot(out var after),"restored snapshot available");
                foreach(var f in before.Formats)Check(after.Formats.Any(x=>x.Key==f.Key&&x.Value.SequenceEqual(f.Value)),"restored exact bytes format "+f.Key);
                foreach(string name in new[]{"CanIncludeInClipboardHistory","CanUploadToCloudClipboard"})Check(after.Formats.Single(x=>x.Key==ClipboardTransfer.RegisterClipboardFormat(name)).Value.Take(4).All(x=>x==0),name+" DWORD zero");
                Prepare();var delayed=new CommandBinding(ApplicationCommands.Paste,async(a,b)=>{b.Handled=true;await Task.Delay(650);box.SelectedText=Clipboard.GetText();});box.CommandBindings.Add(delayed);
                delivered=await ClipboardInserter.DeliverAsync("Verzögerter Abruf mit Großbuchstaben.",OutputMode.Insert);box.CommandBindings.Remove(delayed);
                Check(delivered.Success&&box.Text=="Verzögerter Abruf mit Großbuchstaben.","target reading after 650 ms still receives dictation");
                Prepare();TextChangedEventHandler copy=(a,b)=>{if(!Newer&&box.Text.Length>0){Newer=true;initial.SetExcluded(new[]{new KeyValuePair<uint,byte[]>(13,Utf("Neu kopiert"))});}};box.TextChanged+=copy;
                delivered=await ClipboardInserter.DeliverAsync("Neue Kopie gewinnt.",OutputMode.Insert);box.TextChanged-=copy;
                log.Add("RACE success="+delivered.Success+" method="+delivered.Method+" error="+delivered.Error+" newer="+Newer+" actual="+Clipboard.GetText());Check(delivered.Success&&Clipboard.GetText()=="Neu kopiert","new user copy wins over restoration");
                initial.SetExcluded(new[]{new KeyValuePair<uint,byte[]>(ClipboardTransfer.RegisterClipboardFormat("LocalDictation.Audit.Unknown"),new byte[]{1,2,3})});box.Clear();box.Focus();Keyboard.Focus(box);uint seq=ClipboardTransfer.GetClipboardSequenceNumber();
                delivered=await ClipboardInserter.DeliverAsync("Große Straße 😀.",OutputMode.Insert);await Task.Delay(300);
                Check(delivered.Success&&delivered.Method=="unicode-fallback"&&box.Text=="Große Straße 😀.","unknown format uses Unicode fallback with exact case");Check(seq==ClipboardTransfer.GetClipboardSequenceNumber(),"fallback never changes clipboard sequence");
                await Task.Delay(500);var historyAfter=await Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync();
                if(historyBefore.Status==Windows.ApplicationModel.DataTransfer.ClipboardHistoryItemsResultStatus.Success && historyAfter.Status==Windows.ApplicationModel.DataTransfer.ClipboardHistoryItemsResultStatus.Success)Check(historyBefore.Items.Select(x=>x.Id).SequenceEqual(historyAfter.Items.Select(x=>x.Id)),"Windows clipboard history IDs and order unchanged");else log.Add("HISTORY unavailable: "+historyBefore.Status+" / "+historyAfter.Status);
                label.Text="PASS: "+log.Count(x=>x.StartsWith("PASS"))+" Prüfungen. "+log.First(x=>x.StartsWith("MEASURE"));
            }catch(Exception ex){log.Add("FAIL "+ex);label.Text="FAIL: "+ex.Message;}
            finally{initial.SetExcluded(original.Formats);File.WriteAllLines(Path.Combine(data,"clipboard-result.txt"),log);button.IsEnabled=true;}
        };
        app.Run(window);
    }
}
