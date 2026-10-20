using System;
using System.IO;
using Splatform;

public static class SaveFileHelper
{
	public class SaveWrite : IDisposable
	{
		private readonly BinaryWriter m_writer;

		private readonly FileWriter m_fileWriter;

		public ZPackage Pkg { get; }

		public SaveWrite(BinaryWriter writer, FileWriter fileWriter, ZPackage pkg)
		{
			m_writer = writer;
			m_fileWriter = fileWriter;
			Pkg = pkg;
		}

		public bool Finish()
		{
			Pkg.Flush();
			m_fileWriter.Finish();
			return m_fileWriter.Status == FileWriter.WriterStatus.CloseSucceeded;
		}

		public void Dispose()
		{
			m_fileWriter?.Finish();
		}
	}

	public class SaveReader : IDisposable
	{
		private readonly BinaryReader m_reader;

		public ZPackage Pkg { get; }

		public SaveReader(BinaryReader reader, ZPackage pkg)
		{
			m_reader = reader;
			Pkg = pkg;
		}

		public void Dispose()
		{
			m_reader?.Close();
		}
	}

	public static SaveWrite CreateZPackageForWriting(string path, FileHelpers.FileSource fileSource)
	{
		(FileWriter, BinaryWriter) tuple = CreateFileForWriting(path, fileSource);
		FileWriter item = tuple.Item1;
		BinaryWriter item2 = tuple.Item2;
		ZPackage zPackage = new ZPackage();
		zPackage.SetWriter(item2);
		return new SaveWrite(item2, item, zPackage);
	}

	public static SaveReader CreateZPackageForReading(string path, FileHelpers.FileSource fileSource)
	{
		BinaryReader reader = OpenFileForReading(path, fileSource);
		ZPackage zPackage = new ZPackage();
		zPackage.SetReader(reader);
		return new SaveReader(reader, zPackage);
	}

	private static (FileWriter, BinaryWriter) CreateFileForWriting(string path, FileHelpers.FileSource fileSource)
	{
		CloudStorageFileGrouping grouping = ((!SaveSystem.IsWorldSaveFile(path)) ? CloudStorageFileGrouping.SameFileEnding : CloudStorageFileGrouping.SameFolder);
		FileWriter fileWriter = new FileWriter(path, grouping, FileHelpers.FileHelperType.Binary, fileSource);
		BinaryWriter item = new BinaryWriter(new BufferedStream(fileWriter.m_binary.BaseStream, 1048576));
		return (fileWriter, item);
	}

	private static BinaryReader OpenFileForReading(string path, FileHelpers.FileSource fileSource)
	{
		return new BinaryReader(new BufferedStream(new FileReader(path, fileSource).m_binary.BaseStream, 1048576));
	}
}
