using System;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed partial class SentinelAdminPanel
    {
        private string _toolReportText="",_toolReportPath="",_toolReportTitle="";
        private Vector2 _toolReportScroll;
        private void ReceiveToolReport(string message)
        {
            try
            {
                var report=SentinelJson.Read<SentinelToolReport>(message);
                if(report==null||string.IsNullOrEmpty(report.file)||report.file!=Path.GetFileName(report.file))throw new FormatException("Invalid report filename");
                _requesting=true;_toolReportTitle=report.title;_status=PT("Downloading report to your machine…");
                DownloadToolPart(report,new MemoryStream(),ZNet.instance);
            }
            catch(Exception error){_requesting=false;_status=PT("Report download failed")+": "+error.Message;}
        }
        private void DownloadToolPart(SentinelToolReport report,MemoryStream buffer,ZNet connection)
        {
            int offset=(int)buffer.Length;
            _control.DownloadReport(report.file,offset,(ok,message)=>Enqueue(()=>
            {
                try
                {
                    if(!ok)throw new IOException(message);
                    if(!ReferenceEquals(connection,ZNet.instance))throw new IOException("Server connection changed.");
                    var chunk=SentinelJson.Read<SentinelReportChunk>(message);
                    if(chunk.offset!=offset||chunk.total>512*1024||chunk.total<offset)throw new IOException("Invalid report chunk.");
                    byte[] bytes=Convert.FromBase64String(chunk.data);
                    if(bytes.Length>32768||offset+bytes.Length>chunk.total||(bytes.Length==0&&offset<chunk.total))throw new IOException("Invalid report chunk length.");
                    buffer.Write(bytes,0,bytes.Length);
                    if(buffer.Length<chunk.total){DownloadToolPart(report,buffer,connection);return;}
                    string folder=Path.Combine(Paths.ConfigPath,"RunicSentinel","Reports");Directory.CreateDirectory(folder);
                    _toolReportPath=Path.Combine(folder,report.file);File.WriteAllBytes(_toolReportPath,buffer.ToArray());
                    _toolReportText=Encoding.UTF8.GetString(buffer.ToArray());buffer.Dispose();_requesting=false;
                    _status=PT("Report saved locally")+": "+_toolReportPath;
                }
                catch(Exception error){buffer.Dispose();_requesting=false;_status=PT("Report download failed")+": "+error.Message;}
            }));
        }
        private void DrawReportViewer()
        {
            if(_toolReportText.Length==0)return;
            GUILayout.Space(10);GUILayout.Label(_toolReportTitle,_heading);
            GUILayout.Label(_toolReportPath,_label);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(PT("Copy local file path"),_button))GUIUtility.systemCopyBuffer=_toolReportPath;
            if(GUILayout.Button(PT("Copy report text"),_button))GUIUtility.systemCopyBuffer=_toolReportText;
            GUILayout.EndHorizontal();
            _toolReportScroll=GUILayout.BeginScrollView(_toolReportScroll,GUILayout.Height(300));
            GUILayout.TextArea(_toolReportText,new GUIStyle(_textArea){wordWrap=true,richText=false});GUILayout.EndScrollView();
        }
    }
}
