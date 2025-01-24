package transaction_pix_repository_interfaces

import transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"

type ITransactionPixRepository interface {
	Save(transaction *transaction_pix.TransactionPix)
}
