using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Sync;

public class SyncController
{
	private Dictionary<RefID, SyncElement> syncElements;
	private List<SyncElement> dirtySyncElements;
	private readonly object _dirtyLock = new object();

	public World Owner { get; private set; }

	public SyncController(World owner)
	{
		Owner = owner;
		syncElements = new Dictionary<RefID, SyncElement>();
		dirtySyncElements = new List<SyncElement>();
	}

	public void RegisterSyncElement(SyncElement element)
	{
		syncElements.Add(element.ReferenceID, element);
	}

	public void UnregisterSyncElement(SyncElement element)
	{
		syncElements.Remove(element.ReferenceID);
	}

	public void AddDirtySyncElement(SyncElement element)
	{
		if (Owner.State != World.WorldState.Running && !Owner.IsAuthority)
		{
			throw new Exception("Cannot Process Dirty Elements when not Running");
		}
		lock (_dirtyLock)
		{
			dirtySyncElements.Add(element);
		}
	}

	public DeltaBatch CollectDeltaMessages()
	{
		DeltaBatch deltaBatch = new DeltaBatch(Owner.StateVersion, Owner.SyncTick);
		List<SyncElement> elementsToSend;
		lock (_dirtyLock)
		{
			if (dirtySyncElements.Count == 0)
			{
				return deltaBatch;
			}

			elementsToSend = new List<SyncElement>(dirtySyncElements);
			dirtySyncElements.Clear();
		}

		elementsToSend.Sort((SyncElement a, SyncElement b) => a.ReferenceID.CompareTo(b.ReferenceID));
		foreach (SyncElement dirtySyncElement in elementsToSend)
		{
			BinaryWriter writer = deltaBatch.BeginNewDataRecord(dirtySyncElement.ReferenceID);
			dirtySyncElement.EncodeDelta(writer, deltaBatch);
			deltaBatch.FinishDataRecord(dirtySyncElement.ReferenceID);
		}

		return deltaBatch;
	}

	public FullBatch EncodeFullBatch()
	{
		return EncodeFullBatch(syncElements.Values);
	}

	public FullBatch EncodeFullBatch(IEnumerable<SyncElement> elements)
	{
		FullBatch fullBatch = new FullBatch(Owner.StateVersion, Owner.SyncTick);
		List<SyncElement> list = new List<SyncElement>();
		foreach (var element in elements)
		{
			list.Add(element);
		}
		list.Sort((SyncElement a, SyncElement b) => a.ReferenceID.CompareTo(b.ReferenceID));
		foreach (SyncElement item in list)
		{
			BinaryWriter writer = fullBatch.BeginNewDataRecord(item.ReferenceID);
			item.EncodeFull(writer, fullBatch);
			fullBatch.FinishDataRecord(item.ReferenceID);
		}
		return fullBatch;
	}

	public void EncodeFull(RefID id, BinaryMessageBatch syncMessage)
	{
		BinaryWriter writer = syncMessage.BeginNewDataRecord(id);
		syncElements[id].EncodeFull(writer, syncMessage);
		syncMessage.FinishDataRecord(id);
	}

	public void ValidateDeltaMessages(DeltaBatch batch)
	{
		List<ValidationGroup.Rule> list = new List<ValidationGroup.Rule>();
		List<ValidationGroup> list2 = new List<ValidationGroup>();
		for (int i = 0; i < batch.DataRecordCount; i++)
		{
			DataRecord dataRecord = batch.GetDataRecord(i);
			if (syncElements.TryGetValue(dataRecord.TargetID, out var value))
			{
				BinaryReader reader = batch.SeekDataRecord(i);
				MessageValidity messageValidity = value.Validate(batch, reader, list);
				if (messageValidity != MessageValidity.Valid)
				{
					batch.InvalidateDataRecord(i, messageValidity == MessageValidity.Conflict);
				}
				if (list.Count > 0)
				{
					ValidationGroup item = new ValidationGroup();
					item.Set(i, value, list, batch, Owner);
					list2.Add(item);
					list = new List<ValidationGroup.Rule>();
				}
			}
		}
		Dictionary<RefID, int> dictionary = new Dictionary<RefID, int>();
		foreach (ValidationGroup item2 in list2)
		{
			foreach (ValidationGroup.Rule validationRule in item2.ValidationRules)
			{
				RefID otherMessage = validationRule.OtherMessage;
				if (!dictionary.ContainsKey(otherMessage))
				{
					dictionary.Add(otherMessage, batch.FindDataRecordIndex(otherMessage));
				}
			}
		}
		foreach (ValidationGroup item3 in list2)
		{
			bool flag = batch.GetDataRecord(item3.RequestingRecordIndex).Validity == MessageValidity.Conflict;
			List<int> list3 = new List<int>();
			foreach (ValidationGroup.Rule validationRule2 in item3.ValidationRules)
			{
				int num = dictionary[validationRule2.OtherMessage];
				if (num < 0)
				{
					if (validationRule2.MustExist)
					{
						flag = true;
					}
					continue;
				}
				DataRecord dataRecord2 = batch.GetDataRecord(num);
				list3.Add(num);
				if (dataRecord2.Validity == MessageValidity.Conflict)
				{
					flag = true;
				}
				if (validationRule2.CustomValidation != null)
				{
					BinaryReader arg = batch.SeekDataRecord(num);
					if (!validationRule2.CustomValidation(arg))
					{
						flag = true;
					}
				}
			}
			if (!flag)
			{
				continue;
			}
			batch.InvalidateDataRecord(item3.RequestingRecordIndex, conflict: true);
			item3.RequestingSyncElement.Invalidate();
			foreach (int item4 in list3)
			{
				batch.InvalidateDataRecord(item4, conflict: true);
				DataRecord dataRecord3 = batch.GetDataRecord(item4);
				if (syncElements.TryGetValue(dataRecord3.TargetID, out var value2))
				{
					value2.Invalidate();
				}
			}
		}
	}

	public void ApplyConfirmations(IEnumerable<RefID> ids, ulong confirmTime)
	{
		foreach (RefID id in ids)
		{
			if (syncElements.TryGetValue(id, out var value))
			{
				value.Confirm(confirmTime);
			}
		}
	}

	public bool DecodeDeltaMessage(int recordIndex, DeltaBatch batch)
	{
		return DecodeBinaryMessage(recordIndex, batch, isFull: false);
	}

	public bool DecodeFullMessage(int recordIndex, FullBatch batch)
	{
		return DecodeBinaryMessage(recordIndex, batch, isFull: true);
	}

	public bool DecodeCorrection(int recordIndex, ConfirmationMessage batch)
	{
		return DecodeBinaryMessage(recordIndex, batch, isFull: true);
	}

	private bool DecodeBinaryMessage(int recordIndex, BinaryMessageBatch message, bool isFull)
	{
		DataRecord dataRecord = message.GetDataRecord(recordIndex);
		if (syncElements.TryGetValue(dataRecord.TargetID, out var value))
		{
			BinaryReader reader = message.SeekDataRecord(recordIndex);
			try
			{
				if (!isFull)
				{
					value.DecodeDelta(reader, message);
				}
				else
				{
					value.DecodeFull(reader, message);
				}
				return true;
			}
			catch (Exception ex)
			{
				AquaLogger.Error($"DecodeBinaryMessage: Failed to decode {dataRecord.TargetID}: {ex.Message}");
				return false;
			}
		}
		else
		{
			// Try to get the object from ReferenceController
			var element = Owner.ReferenceController.GetObjectOrNull(dataRecord.TargetID);
			if (element is SyncElement syncElement)
			{
				// Register it with sync controller
				RegisterSyncElement(syncElement);
				
				// Try decoding again
				BinaryReader reader = message.SeekDataRecord(recordIndex);
				try
				{
					if (!isFull)
					{
						syncElement.DecodeDelta(reader, message);
					}
					else
					{
						syncElement.DecodeFull(reader, message);
					}
					return true;
				}
				catch (Exception ex)
				{
					AquaLogger.Error($"DecodeBinaryMessage: Failed to decode registered element {dataRecord.TargetID}: {ex.Message}");
					return false;
				}
			}
		}
		return false;
	}

	public void GatherStreams(List<StreamMessage> messages)
	{
	}

	public void ApplyStreams(StreamMessage message)
	{
	}

	public void Dispose()
	{
		syncElements.Clear();
		lock (_dirtyLock)
		{
			dirtySyncElements.Clear();
		}
	}
}
