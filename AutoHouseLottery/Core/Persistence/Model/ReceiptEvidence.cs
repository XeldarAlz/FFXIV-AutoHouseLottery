namespace AutoHouseLottery.Core.Persistence.Model;

// NativeConfirmation: correlated live after this plugin's own confirmation. Reconciled: an uncertain attempt of this
// plugin later found in the character's lottery reading. ImportedFromGame: an entry the reading showed that no attempt
// of this plugin made, with every field the reading did not supply left unknown.
internal enum ReceiptEvidence : byte { NativeConfirmation, Reconciled, ImportedFromGame }
