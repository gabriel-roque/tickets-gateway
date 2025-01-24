package transaction_pix_handlers

import (
	repository_interfaces "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/interfaces"
	repository "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/repository"
	interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
)

type createTransactionPixHandler struct {
	repository repository_interfaces.ITransactionPixRepository
}

func NewCreateTransactionPixHandler() *createTransactionPixHandler {
	repository := repository.NewCreateTransactionPixRepository()

	return &createTransactionPixHandler{
		repository,
	}
}

func (u *createTransactionPixHandler) Execute(transaction interfaces.CreateTransactionPix) {
	u.repository.Save(&transaction)
}
