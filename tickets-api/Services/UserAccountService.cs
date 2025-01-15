using Microsoft.AspNetCore.Identity;
using TicketsApi.AppConfig.Errors;
using TicketsApi.Dtos;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Services;

public class UserAccountService(
    UserManager<User> userManager,
    RoleManager<IdentityRole> roleManager,
    ITokenService tokenService
) : IUserAccountService
{
    public async Task<ServiceResponse.CreateAccount> CreateAccount(UserDto userDto)
    {
        if (userDto is null) throw new UnprocessedEntityException("User is empty");
        
        User newUser = new User()
        {
            Name = userDto.Name,
            Email = userDto.Email,
            PasswordHash = userDto.Password,
            UserName = userDto.Email
        };
        
        User user = await userManager.FindByEmailAsync(newUser.Email);
        if (user is not null) throw new UnprocessedEntityException("User registered already");

        IdentityResult createUser = await userManager.CreateAsync(newUser!, userDto.Password);
        if (!createUser.Succeeded) throw new BadRequestException("Error occured.. please try again");

        // Assign Default Role : Admin to first registrar; rest is user (Only logic for study and test)
        IdentityRole checkAdmin = await roleManager.FindByNameAsync(Roles.ADMIN);
        if (checkAdmin is null)
        {
            await roleManager.CreateAsync(new IdentityRole() { Name = Roles.ADMIN });
            await userManager.AddToRoleAsync(newUser, Roles.ADMIN);
            return new ServiceResponse.CreateAccount("Account Created");
        }
        
        IdentityRole checkUser = await roleManager.FindByNameAsync(Roles.USER);
        if (checkUser is null)
            await roleManager.CreateAsync(new IdentityRole() { Name = Roles.USER });

        await userManager.AddToRoleAsync(newUser, Roles.USER);
        return new ServiceResponse.CreateAccount("Account Created");
    }

    public async Task<ServiceResponse.LoginResponse> LoginAccount(LoginDto loginDto)
    {
        if (loginDto is null)
            return new ServiceResponse.LoginResponse(null!, "Login container is empty");

        User getUser = await userManager.FindByEmailAsync(loginDto.Email);
        if (getUser is null) throw new NotFoundException("User not found");

        bool checkUserPasswords = await userManager.CheckPasswordAsync(getUser, loginDto.Password);
        if (!checkUserPasswords)
            throw new UnauthorizationException("Invalid email/password");

        var getUserRole = await userManager.GetRolesAsync(getUser);
        var userSession = new UserSession(getUser.Id, getUser.Name, getUser.Email, getUserRole.First());

        string token = tokenService.Generate(userSession);
        
        return new ServiceResponse.LoginResponse(token!, "Login completed");
    }
}