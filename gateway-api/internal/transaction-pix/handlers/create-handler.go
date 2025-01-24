package transaction_pix_handlers

import (
	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	transaction_pix_repository_interfaces "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/interfaces"
	transaction_pix_create_repository "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/repository"
)

type createTransactionPixHandler struct {
	repository transaction_pix_repository_interfaces.ITransactionPixRepository
}

func NewCreateTransactionPixHandler() *createTransactionPixHandler {
	repository := transaction_pix_create_repository.NewCreateTransactionPixRepository()

	return &createTransactionPixHandler{
		repository,
	}
}

func (u *createTransactionPixHandler) Execute(transaction transaction_pix.TransactionPix) {
	u.repository.Save(&transaction)
}
