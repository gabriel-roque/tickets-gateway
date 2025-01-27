package transaction_pix_handlers

import (
	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
)

func (u *TransactionPixHandler) Create(transaction interfaces.CreateTransactionPix) transaction_pix.TransactionPix {
	return u.repository.Save(&transaction)
}
