package use_cases

import (
	"fmt"

	"github.com/gabriel-roque/tickets-gateway/internal/entities"
	"github.com/gabriel-roque/tickets-gateway/internal/repositories"
)

type createCategoryUseCase struct {
	repository repositories.ICreateCategoryRepository
}

func NewCreateCategoryUseCase(repository repositories.ICreateCategoryRepository) *createCategoryUseCase {
	return &createCategoryUseCase{repository}
}

func (u *createCategoryUseCase) Execute(name string) error {
	category, err := entities.NewCategory(name)

	if err != nil {
		return err
	}

	fmt.Println(category)
	u.repository.Save(category)

	return nil
}
